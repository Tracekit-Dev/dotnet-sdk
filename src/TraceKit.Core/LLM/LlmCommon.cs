using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TraceKit.Core.LLM;

/// <summary>
/// Shared helpers for LLM instrumentation: PII scrubbing, attribute setting,
/// provider detection, and content capture.
/// </summary>
public static class LlmCommon
{
    /// <summary>
    /// ActivitySource for all LLM instrumentation spans.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("tracekit-llm");

    // Pre-compiled PII patterns. All replace with plain [REDACTED] per project convention.
    private static readonly Regex[] PiiPatterns =
    {
        new(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled),
        new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled),
        new(@"\b\d{4}[- ]?\d{4}[- ]?\d{4}[- ]?\d{4}\b", RegexOptions.Compiled),
        new(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled),
        new(@"(?i)(?:bearer\s+)[A-Za-z0-9._~+/=\-]{20,}", RegexOptions.Compiled),
        new(@"sk_live_[0-9a-zA-Z]{10,}", RegexOptions.Compiled),
        new(@"eyJ[A-Za-z0-9_\-]+\.eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+", RegexOptions.Compiled),
        new(@"-----BEGIN (?:RSA |EC )?PRIVATE KEY-----", RegexOptions.Compiled),
    };

    /// <summary>
    /// Pattern for detecting sensitive JSON property names.
    /// </summary>
    private static readonly Regex SensitiveKeyPattern =
        new(@"(?i)^(password|passwd|pwd|secret|token|key|credential|api_key|apikey)$", RegexOptions.Compiled);

    /// <summary>
    /// Scrubs PII from a string by applying all pattern-based replacements.
    /// </summary>
    public static string ScrubPii(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        foreach (var pattern in PiiPatterns)
        {
            content = pattern.Replace(content, "[REDACTED]");
        }

        return content;
    }

    /// <summary>
    /// Scrubs sensitive JSON property values by key name.
    /// Properties matching the sensitive key pattern get their values replaced with "[REDACTED]".
    /// </summary>
    public static string ScrubJsonKeys(string json)
    {
        if (string.IsNullOrEmpty(json))
            return json;

        try
        {
            using var doc = JsonDocument.Parse(json);
            using var stream = new System.IO.MemoryStream();
            using var writer = new Utf8JsonWriter(stream);
            ScrubJsonElement(writer, doc.RootElement, parentPropertyName: null);
            writer.Flush();
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            // If JSON parse fails, return original
            return json;
        }
    }

    private static void ScrubJsonElement(Utf8JsonWriter writer, JsonElement element, string? parentPropertyName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name);
                    if (SensitiveKeyPattern.IsMatch(prop.Name))
                    {
                        writer.WriteStringValue("[REDACTED]");
                    }
                    else
                    {
                        ScrubJsonElement(writer, prop.Value, prop.Name);
                    }
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    ScrubJsonElement(writer, item, null);
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
        }
    }

    /// <summary>
    /// Detects the LLM provider from a request host.
    /// </summary>
    /// <returns>"openai", "anthropic", or null if not recognized.</returns>
    public static string? DetectProvider(string host)
    {
        if (string.IsNullOrEmpty(host))
            return null;

        // Strip port if present
        var h = host;
        var idx = h.IndexOf(':');
        if (idx >= 0)
            h = h[..idx];

        return h switch
        {
            "api.openai.com" => "openai",
            "api.anthropic.com" => "anthropic",
            _ => null
        };
    }

    /// <summary>
    /// Determines whether content capture is enabled, checking env var first then config.
    /// Env var TRACEKIT_LLM_CAPTURE_CONTENT: "true"/"1" enables, "false"/"0" disables.
    /// </summary>
    public static bool ShouldCaptureContent(LlmConfig config)
    {
        var envVal = Environment.GetEnvironmentVariable("TRACEKIT_LLM_CAPTURE_CONTENT");
        if (!string.IsNullOrEmpty(envVal))
        {
            return string.Equals(envVal, "true", StringComparison.OrdinalIgnoreCase)
                   || envVal == "1";
        }

        return config.CaptureContent;
    }

    /// <summary>
    /// Sets gen_ai.request.* attributes on the span.
    /// </summary>
    public static void SetGenAiRequestAttrs(
        Activity span, string provider, string model,
        int maxTokens, double temperature, double topP)
    {
        span.SetTag("gen_ai.operation.name", "chat");
        span.SetTag("gen_ai.system", provider);
        span.SetTag("gen_ai.request.model", model);

        if (maxTokens > 0)
            span.SetTag("gen_ai.request.max_tokens", maxTokens);
        if (temperature > 0)
            span.SetTag("gen_ai.request.temperature", temperature);
        if (topP > 0)
            span.SetTag("gen_ai.request.top_p", topP);
    }

    /// <summary>
    /// Sets gen_ai.response.* and gen_ai.usage.* attributes on the span.
    /// </summary>
    public static void SetGenAiResponseAttrs(
        Activity span, string? responseId, string? responseModel,
        List<string> finishReasons, int inputTokens, int outputTokens)
    {
        if (!string.IsNullOrEmpty(responseId))
            span.SetTag("gen_ai.response.id", responseId);
        if (!string.IsNullOrEmpty(responseModel))
            span.SetTag("gen_ai.response.model", responseModel);
        if (finishReasons.Count > 0)
            span.SetTag("gen_ai.response.finish_reasons", string.Join(",", finishReasons));
        if (inputTokens > 0)
            span.SetTag("gen_ai.usage.input_tokens", inputTokens);
        if (outputTokens > 0)
            span.SetTag("gen_ai.usage.output_tokens", outputTokens);
    }

    /// <summary>
    /// Records a tool call as a span event.
    /// </summary>
    public static void RecordToolCallEvent(Activity span, string name, string? callId, string? arguments)
    {
        var tags = new ActivityTagsCollection
        {
            { "gen_ai.tool.name", name }
        };

        if (!string.IsNullOrEmpty(callId))
            tags["gen_ai.tool.call.id"] = callId;
        if (!string.IsNullOrEmpty(arguments))
            tags["gen_ai.tool.call.arguments"] = arguments;

        span.AddEvent(new ActivityEvent("gen_ai.tool.call", tags: tags));
    }

    /// <summary>
    /// Sets span status to Error and records error.type attribute.
    /// </summary>
    public static void SetGenAiErrorAttrs(Activity span, Exception e)
    {
        span.SetStatus(ActivityStatusCode.Error, e.Message);
        span.SetTag("error.type", e.GetType().Name);
    }

    /// <summary>
    /// Captures PII-scrubbed input messages on the span.
    /// </summary>
    public static void CaptureInputMessages(Activity span, string messagesJson)
    {
        var scrubbed = ScrubJsonKeys(ScrubPii(messagesJson));
        span.SetTag("gen_ai.input.messages", scrubbed);
    }

    /// <summary>
    /// Captures PII-scrubbed output messages on the span.
    /// </summary>
    public static void CaptureOutputMessages(Activity span, string contentJson)
    {
        var scrubbed = ScrubJsonKeys(ScrubPii(contentJson));
        span.SetTag("gen_ai.output.messages", scrubbed);
    }

    /// <summary>
    /// Captures PII-scrubbed system instructions on the span.
    /// </summary>
    public static void CaptureSystemInstructions(Activity span, string systemJson)
    {
        var scrubbed = ScrubJsonKeys(ScrubPii(systemJson));
        span.SetTag("gen_ai.system_instructions", scrubbed);
    }
}
