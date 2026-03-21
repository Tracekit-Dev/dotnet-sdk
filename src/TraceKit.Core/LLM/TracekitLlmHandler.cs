using System.Diagnostics;

namespace TraceKit.Core.LLM;

/// <summary>
/// DelegatingHandler that automatically instruments LLM API calls (OpenAI, Anthropic)
/// with OpenTelemetry GenAI semantic conventions.
///
/// Usage:
///   var handler = new TracekitLlmHandler();
///   var httpClient = new HttpClient(handler);
///   // Pass httpClient to OpenAI SDK
/// </summary>
public class TracekitLlmHandler : DelegatingHandler
{
    internal readonly LlmConfig Config;

    /// <summary>
    /// Creates a TracekitLlmHandler with the given config and optional inner handler.
    /// </summary>
    public TracekitLlmHandler(LlmConfig config, HttpMessageHandler? innerHandler = null)
    {
        Config = config;
        InnerHandler = innerHandler ?? new HttpClientHandler();
    }

    /// <summary>
    /// Creates a TracekitLlmHandler with default config and optional inner handler.
    /// </summary>
    public TracekitLlmHandler(HttpMessageHandler? innerHandler = null)
        : this(LlmConfig.Default, innerHandler)
    {
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!Config.Enabled)
            return await base.SendAsync(request, cancellationToken);

        var provider = LlmCommon.DetectProvider(request.RequestUri?.Host ?? "");
        if (provider is null)
            return await base.SendAsync(request, cancellationToken);

        if (provider == "openai" && !Config.OpenAI)
            return await base.SendAsync(request, cancellationToken);
        if (provider == "anthropic" && !Config.Anthropic)
            return await base.SendAsync(request, cancellationToken);

        // Only instrument POST requests (API calls, not model listing GETs)
        if (request.Method != HttpMethod.Post)
            return await base.SendAsync(request, cancellationToken);

        // Read request body
        byte[] body = Array.Empty<byte>();
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            // Replace content so downstream can read it
            var newContent = new ByteArrayContent(body);
            if (request.Content.Headers.ContentType is not null)
                newContent.Headers.ContentType = request.Content.Headers.ContentType;
            request.Content = newContent;
        }

        // Route to provider-specific handler
        return provider switch
        {
            "openai" => await OpenAiHandler.HandleAsync(
                Config, request, body,
                (req, ct) => base.SendAsync(req, ct),
                cancellationToken),

            "anthropic" => await AnthropicHandler.HandleAsync(
                Config, request, body,
                (req, ct) => base.SendAsync(req, ct),
                cancellationToken),

            _ => await base.SendAsync(request, cancellationToken)
        };
    }
}
