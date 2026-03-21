using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TraceKit.Core.LLM;

/// <summary>
/// Handles Anthropic API request/response instrumentation with GenAI span attributes.
/// Supports non-streaming and streaming (SSE) responses with event:/data: line pairs,
/// tool_use block detection, system prompt capture, and cache token attributes.
/// </summary>
internal static class AnthropicHandler
{
    /// <summary>
    /// Instruments an Anthropic API call, creating a gen_ai span with appropriate attributes.
    /// </summary>
    public static async Task<HttpResponseMessage> HandleAsync(
        LlmConfig config,
        HttpRequestMessage request,
        byte[] body,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync,
        CancellationToken ct)
    {
        // Parse request body
        JsonDocument? doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            // Can't parse -- pass through without instrumentation
            return await sendAsync(request, ct);
        }

        using (doc)
        {
            var root = doc.RootElement;

            var model = OpenAiHandler.GetStringProp(root, "model") ?? "unknown";
            var maxTokens = OpenAiHandler.GetIntProp(root, "max_tokens");
            var temperature = OpenAiHandler.GetDoubleProp(root, "temperature");
            var topP = OpenAiHandler.GetDoubleProp(root, "top_p");
            var stream = OpenAiHandler.GetBoolProp(root, "stream");

            // Extract messages and system for content capture
            root.TryGetProperty("messages", out var messagesElement);
            root.TryGetProperty("system", out var systemElement);

            // Start activity (span)
            using var activity = LlmCommon.ActivitySource.StartActivity(
                $"chat {model}", ActivityKind.Client);

            if (activity is null)
            {
                // Not sampled -- pass through
                return await sendAsync(request, ct);
            }

            // Set request attributes
            LlmCommon.SetGenAiRequestAttrs(activity, "anthropic", model, maxTokens, temperature, topP);

            // Capture input content if enabled
            if (LlmCommon.ShouldCaptureContent(config))
            {
                if (messagesElement.ValueKind != JsonValueKind.Undefined)
                {
                    LlmCommon.CaptureInputMessages(activity, messagesElement.GetRawText());
                }
                if (systemElement.ValueKind != JsonValueKind.Undefined)
                {
                    LlmCommon.CaptureSystemInstructions(activity, systemElement.GetRawText());
                }
            }

            // Execute request
            HttpResponseMessage response;
            try
            {
                response = await sendAsync(request, ct);
            }
            catch (Exception ex)
            {
                LlmCommon.SetGenAiErrorAttrs(activity, ex);
                throw;
            }

            // Handle error status codes
            if ((int)response.StatusCode >= 400)
            {
                activity.SetTag("http.status_code", (int)response.StatusCode);
                return response;
            }

            if (stream)
            {
                // Wrap response content for streaming SSE interception
                response.Content = new AnthropicStreamContent(
                    response.Content, activity, config);
                // Marker so using block doesn't dispose -- stream content manages lifetime
                activity.SetTag("_streaming", true);
            }
            else
            {
                // Non-streaming: read, parse, set response attributes
                await HandleNonStreamingResponse(activity, config, response);
            }

            return response;
        }
    }

    private static async Task HandleNonStreamingResponse(
        Activity activity, LlmConfig config, HttpResponseMessage response)
    {
        var respBody = await response.Content.ReadAsStringAsync();

        try
        {
            using var respDoc = JsonDocument.Parse(respBody);
            var root = respDoc.RootElement;

            var responseId = OpenAiHandler.GetStringProp(root, "id");
            var responseModel = OpenAiHandler.GetStringProp(root, "model");

            // Extract stop_reason as finish reason
            var finishReasons = new List<string>();
            var stopReason = OpenAiHandler.GetStringProp(root, "stop_reason");
            if (stopReason is not null)
                finishReasons.Add(stopReason);

            // Extract token usage
            int inputTokens = 0, outputTokens = 0;
            int cacheCreation = 0, cacheRead = 0;
            if (root.TryGetProperty("usage", out var usage))
            {
                inputTokens = OpenAiHandler.GetIntProp(usage, "input_tokens");
                outputTokens = OpenAiHandler.GetIntProp(usage, "output_tokens");
                cacheCreation = OpenAiHandler.GetIntProp(usage, "cache_creation_input_tokens");
                cacheRead = OpenAiHandler.GetIntProp(usage, "cache_read_input_tokens");
            }

            LlmCommon.SetGenAiResponseAttrs(activity, responseId, responseModel,
                finishReasons, inputTokens, outputTokens);

            // Set cache token attributes if present
            if (cacheCreation > 0)
                activity.SetTag("gen_ai.usage.cache_creation.input_tokens", cacheCreation);
            if (cacheRead > 0)
                activity.SetTag("gen_ai.usage.cache_read.input_tokens", cacheRead);

            // Record tool_use content blocks as events
            if (root.TryGetProperty("content", out var content))
            {
                foreach (var block in content.EnumerateArray())
                {
                    var blockType = OpenAiHandler.GetStringProp(block, "type");
                    if (blockType == "tool_use")
                    {
                        var name = OpenAiHandler.GetStringProp(block, "name");
                        var callId = OpenAiHandler.GetStringProp(block, "id");
                        string? arguments = null;
                        if (block.TryGetProperty("input", out var input))
                        {
                            arguments = input.GetRawText();
                        }
                        if (!string.IsNullOrEmpty(name))
                        {
                            LlmCommon.RecordToolCallEvent(activity, name!, callId, arguments);
                        }
                    }
                }

                // Capture output content if enabled
                if (LlmCommon.ShouldCaptureContent(config) && content.GetArrayLength() > 0)
                {
                    LlmCommon.CaptureOutputMessages(activity, content.GetRawText());
                }
            }
        }
        catch (JsonException)
        {
            // Response parse failed -- span still ends with whatever attributes were set
        }

        // Rebuild response so caller can read it
        response.Content = new StringContent(respBody, Encoding.UTF8, "application/json");
    }
}

/// <summary>
/// Wraps an Anthropic SSE streaming response, transparently passing through all data
/// while accumulating GenAI attributes from SSE event:/data: line pairs.
/// Anthropic SSE format: event: [type]\ndata: {...}\n\n
/// When the stream ends, sets final attributes and disposes the activity.
/// </summary>
internal class AnthropicStreamContent : HttpContent
{
    private readonly HttpContent _originalContent;
    private readonly Activity _activity;
    private readonly LlmConfig _config;

    // Accumulated streaming state
    private string? _responseId;
    private string? _responseModel;
    private string? _stopReason;
    private int _inputTokens;
    private int _outputTokens;
    private int _cacheCreation;
    private int _cacheRead;

    // Track current event type from event: lines
    private string? _currentEventType;

    public AnthropicStreamContent(HttpContent originalContent, Activity activity, LlmConfig config)
    {
        _originalContent = originalContent;
        _activity = activity;
        _config = config;

        // Copy headers from original content
        foreach (var header in originalContent.Headers)
        {
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        using var originalStream = await _originalContent.ReadAsStreamAsync();
        using var reader = new StreamReader(originalStream);
        var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = false };

        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                // Parse SSE lines for GenAI attributes
                ParseSseLine(line);

                // Write line to output stream transparently
                await writer.WriteAsync(line);
                await writer.WriteAsync('\n');
                await writer.FlushAsync();
            }
        }
        finally
        {
            // Stream ended -- finalize span
            FinalizeSpan();
            await writer.DisposeAsync();
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }

    private void ParseSseLine(string line)
    {
        var trimmed = line.Trim();

        // Track event type from event: lines
        if (trimmed.StartsWith("event: "))
        {
            _currentEventType = trimmed["event: ".Length..].Trim();
            return;
        }

        if (!trimmed.StartsWith("data: "))
            return;

        var data = trimmed["data: ".Length..];

        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            // Use the type from the data payload, or fall back to tracked event type
            var eventType = OpenAiHandler.GetStringProp(root, "type") ?? _currentEventType;

            switch (eventType)
            {
                case "message_start":
                    // message_start contains message object with id, model, usage
                    if (root.TryGetProperty("message", out var message))
                    {
                        var id = OpenAiHandler.GetStringProp(message, "id");
                        if (id is not null) _responseId = id;

                        var model = OpenAiHandler.GetStringProp(message, "model");
                        if (model is not null) _responseModel = model;

                        if (message.TryGetProperty("usage", out var usage))
                        {
                            var inp = OpenAiHandler.GetIntProp(usage, "input_tokens");
                            if (inp > 0) _inputTokens = inp;

                            _cacheCreation = OpenAiHandler.GetIntProp(usage, "cache_creation_input_tokens");
                            _cacheRead = OpenAiHandler.GetIntProp(usage, "cache_read_input_tokens");
                        }
                    }
                    break;

                case "message_delta":
                    // message_delta contains delta.stop_reason and usage.output_tokens
                    if (root.TryGetProperty("delta", out var delta))
                    {
                        var stopReason = OpenAiHandler.GetStringProp(delta, "stop_reason");
                        if (stopReason is not null) _stopReason = stopReason;
                    }
                    if (root.TryGetProperty("usage", out var deltaUsage))
                    {
                        var outp = OpenAiHandler.GetIntProp(deltaUsage, "output_tokens");
                        if (outp > 0) _outputTokens = outp;
                    }
                    break;

                case "content_block_start":
                case "content_block_delta":
                case "message_stop":
                    // No action needed for basic instrumentation
                    break;
            }
        }
        catch (JsonException)
        {
            // Ignore unparseable chunks
        }
    }

    private void FinalizeSpan()
    {
        var finishReasons = new List<string>();
        if (_stopReason is not null)
            finishReasons.Add(_stopReason);

        LlmCommon.SetGenAiResponseAttrs(
            _activity, _responseId, _responseModel,
            finishReasons, _inputTokens, _outputTokens);

        // Set cache token attributes if present
        if (_cacheCreation > 0)
            _activity.SetTag("gen_ai.usage.cache_creation.input_tokens", _cacheCreation);
        if (_cacheRead > 0)
            _activity.SetTag("gen_ai.usage.cache_read.input_tokens", _cacheRead);

        _activity.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _originalContent.Dispose();
        }
        base.Dispose(disposing);
    }
}
