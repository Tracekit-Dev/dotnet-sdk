using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TraceKit.Core.LLM;

/// <summary>
/// Handles OpenAI API request/response instrumentation with GenAI span attributes.
/// Supports non-streaming and streaming (SSE) responses, tool calls, and content capture.
/// </summary>
internal static class OpenAiHandler
{
    /// <summary>
    /// Instruments an OpenAI API call, creating a gen_ai span with appropriate attributes.
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

            var model = GetStringProp(root, "model") ?? "unknown";
            var maxTokens = GetIntProp(root, "max_tokens");
            var temperature = GetDoubleProp(root, "temperature");
            var topP = GetDoubleProp(root, "top_p");
            var stream = GetBoolProp(root, "stream");

            // Start activity (span)
            using var activity = LlmCommon.ActivitySource.StartActivity(
                $"chat {model}", ActivityKind.Client);

            if (activity is null)
            {
                // Not sampled -- pass through
                return await sendAsync(request, ct);
            }

            // Set request attributes
            LlmCommon.SetGenAiRequestAttrs(activity, "openai", model, maxTokens, temperature, topP);

            // Capture input content if enabled
            if (LlmCommon.ShouldCaptureContent(config) && root.TryGetProperty("messages", out var messages))
            {
                LlmCommon.CaptureInputMessages(activity, messages.GetRawText());
            }

            // For streaming, inject stream_options.include_usage=true if not present
            if (stream)
            {
                body = InjectStreamUsage(body, root);
                var newContent = new ByteArrayContent(body);
                if (request.Content?.Headers.ContentType is not null)
                    newContent.Headers.ContentType = request.Content.Headers.ContentType;
                request.Content = newContent;
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
                response.Content = new OpenAiStreamContent(
                    response.Content, activity, config);
                // Detach activity from using block -- OpenAiStreamContent manages its lifetime
                activity.SetTag("_streaming", true); // marker, activity disposed by stream content
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

            var responseId = GetStringProp(root, "id");
            var responseModel = GetStringProp(root, "model");

            // Extract finish reasons
            var finishReasons = new List<string>();
            if (root.TryGetProperty("choices", out var choices))
            {
                foreach (var choice in choices.EnumerateArray())
                {
                    var reason = GetStringProp(choice, "finish_reason");
                    if (reason is not null)
                        finishReasons.Add(reason);

                    // Record tool calls
                    if (choice.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("tool_calls", out var toolCalls))
                    {
                        foreach (var tc in toolCalls.EnumerateArray())
                        {
                            RecordToolCall(activity, tc);
                        }
                    }
                }
            }

            // Extract token usage
            int inputTokens = 0, outputTokens = 0;
            if (root.TryGetProperty("usage", out var usage))
            {
                inputTokens = GetIntProp(usage, "prompt_tokens");
                outputTokens = GetIntProp(usage, "completion_tokens");
            }

            LlmCommon.SetGenAiResponseAttrs(activity, responseId, responseModel,
                finishReasons, inputTokens, outputTokens);

            // Capture output content if enabled
            if (LlmCommon.ShouldCaptureContent(config) &&
                root.TryGetProperty("choices", out var choicesForCapture))
            {
                LlmCommon.CaptureOutputMessages(activity, choicesForCapture.GetRawText());
            }
        }
        catch (JsonException)
        {
            // Response parse failed -- span still ends with whatever attributes were set
        }

        // Rebuild response so caller can read it
        response.Content = new StringContent(respBody, Encoding.UTF8, "application/json");
    }

    private static void RecordToolCall(Activity activity, JsonElement tc)
    {
        if (!tc.TryGetProperty("function", out var fn))
            return;

        var name = GetStringProp(fn, "name");
        if (string.IsNullOrEmpty(name))
            return;

        var callId = GetStringProp(tc, "id");
        var arguments = GetStringProp(fn, "arguments");
        LlmCommon.RecordToolCallEvent(activity, name!, callId, arguments);
    }

    /// <summary>
    /// Injects stream_options.include_usage=true into the request body if not already present.
    /// </summary>
    private static byte[] InjectStreamUsage(byte[] body, JsonElement root)
    {
        // Check if already present
        if (root.TryGetProperty("stream_options", out var streamOpts) &&
            streamOpts.TryGetProperty("include_usage", out var includeUsage) &&
            includeUsage.ValueKind == JsonValueKind.True)
        {
            return body;
        }

        try
        {
            // Parse as mutable dictionary, inject stream_options
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);
            if (dict is null) return body;

            // Build new stream_options
            var soDict = new Dictionary<string, object>();
            if (root.TryGetProperty("stream_options", out var existingOpts))
            {
                foreach (var prop in existingOpts.EnumerateObject())
                {
                    if (prop.Name != "include_usage")
                        soDict[prop.Name] = prop.Value;
                }
            }
            soDict["include_usage"] = true;

            // Replace in original body using raw JSON manipulation
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            foreach (var kvp in dict)
            {
                if (kvp.Key == "stream_options") continue;
                writer.WritePropertyName(kvp.Key);
                kvp.Value.WriteTo(writer);
            }
            writer.WritePropertyName("stream_options");
            JsonSerializer.Serialize(writer, soDict);
            writer.WriteEndObject();
            writer.Flush();

            return stream.ToArray();
        }
        catch
        {
            return body;
        }
    }

    // --- JSON helper methods ---

    internal static string? GetStringProp(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var val) && val.ValueKind == JsonValueKind.String)
            return val.GetString();
        return null;
    }

    internal static int GetIntProp(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var val) && val.ValueKind == JsonValueKind.Number)
            return val.GetInt32();
        return 0;
    }

    internal static double GetDoubleProp(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var val) && val.ValueKind == JsonValueKind.Number)
            return val.GetDouble();
        return 0;
    }

    internal static bool GetBoolProp(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var val))
        {
            return val.ValueKind == JsonValueKind.True;
        }
        return false;
    }
}

/// <summary>
/// Wraps an OpenAI SSE streaming response, transparently passing through all data
/// while accumulating GenAI attributes from SSE chunks.
/// When the stream ends, sets final attributes and disposes the activity.
/// </summary>
internal class OpenAiStreamContent : HttpContent
{
    private readonly HttpContent _originalContent;
    private readonly Activity _activity;
    private readonly LlmConfig _config;

    // Accumulated streaming state
    private string? _responseId;
    private string? _responseModel;
    private readonly List<string> _finishReasons = new();
    private int _inputTokens;
    private int _outputTokens;

    public OpenAiStreamContent(HttpContent originalContent, Activity activity, LlmConfig config)
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
                // Parse SSE data lines for GenAI attributes
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
        if (!trimmed.StartsWith("data: "))
            return;

        var data = trimmed["data: ".Length..];
        if (data == "[DONE]")
            return;

        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            var id = OpenAiHandler.GetStringProp(root, "id");
            if (id is not null)
                _responseId = id;

            var model = OpenAiHandler.GetStringProp(root, "model");
            if (model is not null)
                _responseModel = model;

            // Extract finish reasons from choices
            if (root.TryGetProperty("choices", out var choices))
            {
                foreach (var choice in choices.EnumerateArray())
                {
                    var reason = OpenAiHandler.GetStringProp(choice, "finish_reason");
                    if (reason is not null)
                        _finishReasons.Add(reason);
                }
            }

            // Extract token usage
            if (root.TryGetProperty("usage", out var usage))
            {
                var inp = OpenAiHandler.GetIntProp(usage, "prompt_tokens");
                var outp = OpenAiHandler.GetIntProp(usage, "completion_tokens");
                if (inp > 0) _inputTokens = inp;
                if (outp > 0) _outputTokens = outp;
            }
        }
        catch (JsonException)
        {
            // Ignore unparseable chunks
        }
    }

    private void FinalizeSpan()
    {
        LlmCommon.SetGenAiResponseAttrs(
            _activity, _responseId, _responseModel,
            _finishReasons, _inputTokens, _outputTokens);
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
