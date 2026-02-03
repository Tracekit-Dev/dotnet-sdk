namespace TraceKit.AspNetCore;

/// <summary>
/// Configuration options for TraceKit ASP.NET Core integration
/// </summary>
public sealed class TracekitOptions
{
    /// <summary>
    /// Whether TraceKit is enabled. Default: true
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// TraceKit API key (required)
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Service name for identification (required)
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// TraceKit endpoint. Default: app.tracekit.dev
    /// </summary>
    public string Endpoint { get; set; } = "app.tracekit.dev";

    /// <summary>
    /// Whether to use SSL. Default: true
    /// </summary>
    public bool UseSSL { get; set; } = true;

    /// <summary>
    /// Environment name (e.g., production, staging, development)
    /// </summary>
    public string Environment { get; set; } = "production";

    /// <summary>
    /// Enable code monitoring with snapshots. Default: true
    /// </summary>
    public bool EnableCodeMonitoring { get; set; } = true;

    /// <summary>
    /// Code monitoring poll interval in seconds. Default: 30
    /// </summary>
    public int CodeMonitoringPollIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Local UI port for development. Default: 9999
    /// </summary>
    public int LocalUIPort { get; set; } = 9999;

    /// <summary>
    /// Validates the configuration and returns validation errors
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (Enabled)
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
                errors.Add("ApiKey is required when TraceKit is enabled");

            if (string.IsNullOrWhiteSpace(ServiceName))
                errors.Add("ServiceName is required when TraceKit is enabled");

            if (string.IsNullOrWhiteSpace(Endpoint))
                errors.Add("Endpoint cannot be empty");
        }

        return errors;
    }
}
