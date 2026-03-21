namespace TraceKit.Core.LLM;

/// <summary>
/// Configuration for LLM auto-instrumentation.
/// Controls which providers are instrumented and whether content capture is enabled.
/// </summary>
public sealed class LlmConfig
{
    /// <summary>
    /// Master toggle for LLM instrumentation. Default: true.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Enables OpenAI-specific instrumentation. Default: true.
    /// </summary>
    public bool OpenAI { get; init; } = true;

    /// <summary>
    /// Enables Anthropic-specific instrumentation. Default: true.
    /// </summary>
    public bool Anthropic { get; init; } = true;

    /// <summary>
    /// Enables capturing prompt/completion content (PII-scrubbed). Default: false.
    /// Can be overridden by TRACEKIT_LLM_CAPTURE_CONTENT env var.
    /// </summary>
    public bool CaptureContent { get; init; } = false;

    /// <summary>
    /// Returns an LlmConfig with sensible defaults.
    /// </summary>
    public static LlmConfig Default => new LlmConfig();
}
