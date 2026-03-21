using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TraceKit.Core.LLM;

/// <summary>
/// HttpPipelinePolicy for Azure OpenAI instrumentation.
/// Add this policy to your Azure.AI.OpenAI client to get gen_ai spans.
///
/// Usage with Azure.AI.OpenAI (requires Azure.Core):
///   var options = new AzureOpenAIClientOptions();
///   options.AddPolicy(new AzureOpenAiPolicy(), HttpPipelinePosition.PerCall);
///   var client = new AzureOpenAIClient(endpoint, credential, options);
///
/// Note: This class is designed to work with Azure.Core.Pipeline.HttpPipelinePolicy.
/// If Azure.Core is not referenced, use TracekitLlmHandler with a custom HttpClient instead.
///
/// The core instrumentation logic is in AzureOpenAiInstrumentation (internal) so it can
/// be tested independently of the Azure SDK pipeline infrastructure.
/// </summary>
// NOTE: The actual HttpPipelinePolicy subclass requires Azure.Core dependency.
// To avoid forcing Azure.Core on all users, the policy base class integration
// is documented here. Users who add Azure.Core can create a thin wrapper:
//
//   public class AzureOpenAiPolicy : HttpPipelinePolicy
//   {
//       public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline) => ...
//       public override async ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline) => ...
//   }
//
// The AzureOpenAiInstrumentation class below provides all the instrumentation logic.
// This file also provides a DelegatingHandler-based approach that works without Azure.Core.

/// <summary>
/// DelegatingHandler that instruments Azure OpenAI HTTP requests.
/// Alternative to HttpPipelinePolicy for users who configure Azure SDK with custom HttpClient.
///
/// Usage:
///   var handler = new AzureOpenAiHandler();
///   var httpClient = new HttpClient(handler);
///   // Configure Azure OpenAI client with this httpClient
/// </summary>
public class AzureOpenAiHandler : DelegatingHandler
{
    private readonly LlmConfig _config;

    public AzureOpenAiHandler(LlmConfig? config = null, HttpMessageHandler? innerHandler = null)
    {
        _config = config ?? LlmConfig.Default;
        InnerHandler = innerHandler ?? new HttpClientHandler();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_config.Enabled || !_config.OpenAI)
            return await base.SendAsync(request, cancellationToken);

        var host = request.RequestUri?.Host ?? "";
        if (!AzureOpenAiInstrumentation.IsAzureOpenAiHost(host))
            return await base.SendAsync(request, cancellationToken);

        if (request.Method != HttpMethod.Post)
            return await base.SendAsync(request, cancellationToken);

        var path = request.RequestUri?.PathAndQuery ?? "";

        // Read request body
        byte[] body = Array.Empty<byte>();
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var newContent = new ByteArrayContent(body);
            if (request.Content.Headers.ContentType is not null)
                newContent.Headers.ContentType = request.Content.Headers.ContentType;
            request.Content = newContent;
        }

        // Try to start instrumentation
        var ctx = AzureOpenAiInstrumentation.TryStartInstrumentation(_config, body, path);
        if (ctx is null)
            return await base.SendAsync(request, cancellationToken);

        // Inject stream_options if streaming
        if (ctx.IsStream)
        {
            body = AzureOpenAiInstrumentation.InjectStreamUsage(body);
            var streamContent = new ByteArrayContent(body);
            if (request.Content?.Headers.ContentType is not null)
                streamContent.Headers.ContentType = request.Content.Headers.ContentType;
            request.Content = streamContent;
        }

        // Execute request
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            LlmCommon.SetGenAiErrorAttrs(ctx.Activity, ex);
            ctx.Activity.Dispose();
            throw;
        }

        // Handle error responses
        if ((int)response.StatusCode >= 400)
        {
            ctx.Activity.SetTag("http.status_code", (int)response.StatusCode);
            ctx.Activity.Dispose();
            return response;
        }

        if (ctx.IsStream)
        {
            // Wrap response for SSE streaming
            response.Content = new AzureOpenAiStreamContent(
                response.Content, ctx.Activity, _config);
        }
        else
        {
            // Non-streaming: parse response and set attributes
            var respBody = await response.Content.ReadAsStringAsync(cancellationToken);
            AzureOpenAiInstrumentation.ProcessNonStreamingResponse(ctx.Activity, _config, respBody);
            response.Content = new StringContent(respBody, Encoding.UTF8, "application/json");
            ctx.Activity.Dispose();
        }

        return response;
    }
}

/// <summary>
/// Internal instrumentation logic for Azure OpenAI, testable without Azure SDK dependencies.
/// Reuses OpenAI JSON parsing since Azure OpenAI uses the same response format.
/// </summary>
internal static class AzureOpenAiInstrumentation
{
    private static readonly Regex DeploymentPattern =
        new(@"/openai/deployments/([^/]+)/", RegexOptions.Compiled);

    /// <summary>
    /// Checks if a host is an Azure OpenAI endpoint (*.openai.azure.com).
    /// </summary>
    public static bool IsAzureOpenAiHost(string host)
    {
        if (string.IsNullOrEmpty(host))
            return false;

        // Strip port if present
        var idx = host.IndexOf(':');
        if (idx >= 0)
            host = host[..idx];

        return host.EndsWith(".openai.azure.com") || host == "openai.azure.com";
    }

    /// <summary>
    /// Extracts the deployment name from an Azure OpenAI URL path.
    /// Pattern: /openai/deployments/{deployment}/...
    /// </summary>
    public static string? ExtractDeploymentName(string path)
    {
        var match = DeploymentPattern.Match(path);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Attempts to start instrumentation. Returns null if disabled or unparseable.
    /// </summary>
    public static InstrumentationContext? TryStartInstrumentation(
        LlmConfig config, byte[] requestBody, string urlPath)
    {
        if (!config.Enabled || !config.OpenAI)
            return null;

        JsonDocument? doc;
        try
        {
            doc = JsonDocument.Parse(requestBody);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            var deploymentName = ExtractDeploymentName(urlPath);
            var model = OpenAiHandler.GetStringProp(root, "model") ?? deploymentName ?? "unknown";
            var maxTokens = OpenAiHandler.GetIntProp(root, "max_tokens");
            var temperature = OpenAiHandler.GetDoubleProp(root, "temperature");
            var topP = OpenAiHandler.GetDoubleProp(root, "top_p");
            var stream = OpenAiHandler.GetBoolProp(root, "stream");

            var activity = LlmCommon.ActivitySource.StartActivity(
                $"chat {model}", ActivityKind.Client);

            if (activity is null)
                return null;

            LlmCommon.SetGenAiRequestAttrs(activity, "openai", model, maxTokens, temperature, topP);

            if (LlmCommon.ShouldCaptureContent(config) &&
                root.TryGetProperty("messages", out var messages))
            {
                LlmCommon.CaptureInputMessages(activity, messages.GetRawText());
            }

            return new InstrumentationContext(activity, stream);
        }
    }

    /// <summary>
    /// Processes a non-streaming response body and sets span attributes.
    /// </summary>
    public static void ProcessNonStreamingResponse(Activity activity, LlmConfig config, string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            var responseId = OpenAiHandler.GetStringProp(root, "id");
            var responseModel = OpenAiHandler.GetStringProp(root, "model");

            var finishReasons = new List<string>();
            if (root.TryGetProperty("choices", out var choices))
            {
                foreach (var choice in choices.EnumerateArray())
                {
                    var reason = OpenAiHandler.GetStringProp(choice, "finish_reason");
                    if (reason is not null)
                        finishReasons.Add(reason);

                    // Tool calls
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

            int inputTokens = 0, outputTokens = 0;
            if (root.TryGetProperty("usage", out var usage))
            {
                inputTokens = OpenAiHandler.GetIntProp(usage, "prompt_tokens");
                outputTokens = OpenAiHandler.GetIntProp(usage, "completion_tokens");
            }

            LlmCommon.SetGenAiResponseAttrs(activity, responseId, responseModel,
                finishReasons, inputTokens, outputTokens);

            if (LlmCommon.ShouldCaptureContent(config) &&
                root.TryGetProperty("choices", out var choicesForCapture))
            {
                LlmCommon.CaptureOutputMessages(activity, choicesForCapture.GetRawText());
            }
        }
        catch (JsonException)
        {
            // Response parse failed -- span ends with whatever attributes were set
        }
    }

    /// <summary>
    /// Test helper: instruments a non-streaming request/response pair in one call.
    /// </summary>
    public static void InstrumentNonStreaming(
        LlmConfig config, byte[] requestBody, byte[] responseBody, int statusCode, string urlPath)
    {
        var ctx = TryStartInstrumentation(config, requestBody, urlPath);
        if (ctx is null)
            return;

        if (statusCode >= 400)
        {
            ctx.Activity.SetTag("http.status_code", statusCode);
            ctx.Activity.Dispose();
            return;
        }

        var responseStr = Encoding.UTF8.GetString(responseBody);
        ProcessNonStreamingResponse(ctx.Activity, config, responseStr);
        ctx.Activity.Dispose();
    }

    /// <summary>
    /// Test helper: instruments a streaming request with pre-parsed SSE lines.
    /// </summary>
    public static void InstrumentStreaming(
        LlmConfig config, byte[] requestBody, string[] sseLines, string urlPath)
    {
        var ctx = TryStartInstrumentation(config, requestBody, urlPath);
        if (ctx is null)
            return;

        // Accumulate SSE state (same logic as OpenAiStreamContent)
        string? responseId = null;
        string? responseModel = null;
        var finishReasons = new List<string>();
        int inputTokens = 0, outputTokens = 0;

        foreach (var line in sseLines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("data: "))
                continue;

            var data = trimmed["data: ".Length..];
            if (data == "[DONE]")
                continue;

            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                var id = OpenAiHandler.GetStringProp(root, "id");
                if (id is not null) responseId = id;

                var model = OpenAiHandler.GetStringProp(root, "model");
                if (model is not null) responseModel = model;

                if (root.TryGetProperty("choices", out var choices))
                {
                    foreach (var choice in choices.EnumerateArray())
                    {
                        var reason = OpenAiHandler.GetStringProp(choice, "finish_reason");
                        if (reason is not null)
                            finishReasons.Add(reason);
                    }
                }

                if (root.TryGetProperty("usage", out var usage))
                {
                    var inp = OpenAiHandler.GetIntProp(usage, "prompt_tokens");
                    var outp = OpenAiHandler.GetIntProp(usage, "completion_tokens");
                    if (inp > 0) inputTokens = inp;
                    if (outp > 0) outputTokens = outp;
                }
            }
            catch (JsonException)
            {
                // Skip unparseable chunks
            }
        }

        LlmCommon.SetGenAiResponseAttrs(ctx.Activity, responseId, responseModel,
            finishReasons, inputTokens, outputTokens);
        ctx.Activity.Dispose();
    }

    /// <summary>
    /// Injects stream_options.include_usage=true into request body.
    /// </summary>
    public static byte[] InjectStreamUsage(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Check if already present
            if (root.TryGetProperty("stream_options", out var streamOpts) &&
                streamOpts.TryGetProperty("include_usage", out var includeUsage) &&
                includeUsage.ValueKind == JsonValueKind.True)
            {
                return body;
            }

            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);
            if (dict is null) return body;

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

    private static void RecordToolCall(Activity activity, JsonElement tc)
    {
        if (!tc.TryGetProperty("function", out var fn))
            return;

        var name = OpenAiHandler.GetStringProp(fn, "name");
        if (string.IsNullOrEmpty(name))
            return;

        var callId = OpenAiHandler.GetStringProp(tc, "id");
        var arguments = OpenAiHandler.GetStringProp(fn, "arguments");
        LlmCommon.RecordToolCallEvent(activity, name!, callId, arguments);
    }

    /// <summary>
    /// Context returned from TryStartInstrumentation.
    /// </summary>
    public sealed class InstrumentationContext
    {
        public Activity Activity { get; }
        public bool IsStream { get; }

        public InstrumentationContext(Activity activity, bool isStream)
        {
            Activity = activity;
            IsStream = isStream;
        }
    }
}

/// <summary>
/// Wraps an Azure OpenAI SSE streaming response, accumulating GenAI attributes.
/// Same logic as OpenAiStreamContent but for the Azure handler.
/// </summary>
internal class AzureOpenAiStreamContent : HttpContent
{
    private readonly HttpContent _originalContent;
    private readonly Activity _activity;
    private readonly LlmConfig _config;

    private string? _responseId;
    private string? _responseModel;
    private readonly List<string> _finishReasons = new();
    private int _inputTokens;
    private int _outputTokens;

    public AzureOpenAiStreamContent(HttpContent originalContent, Activity activity, LlmConfig config)
    {
        _originalContent = originalContent;
        _activity = activity;
        _config = config;

        foreach (var header in originalContent.Headers)
        {
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
    {
        using var originalStream = await _originalContent.ReadAsStreamAsync();
        using var reader = new StreamReader(originalStream);
        var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = false };

        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                ParseSseLine(line);
                await writer.WriteAsync(line);
                await writer.WriteAsync('\n');
                await writer.FlushAsync();
            }
        }
        finally
        {
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
            if (id is not null) _responseId = id;

            var model = OpenAiHandler.GetStringProp(root, "model");
            if (model is not null) _responseModel = model;

            if (root.TryGetProperty("choices", out var choices))
            {
                foreach (var choice in choices.EnumerateArray())
                {
                    var reason = OpenAiHandler.GetStringProp(choice, "finish_reason");
                    if (reason is not null)
                        _finishReasons.Add(reason);
                }
            }

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
            // Skip unparseable chunks
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
