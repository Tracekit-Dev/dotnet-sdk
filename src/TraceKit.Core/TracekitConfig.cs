namespace TraceKit.Core;

/// <summary>
/// Configuration for the TraceKit SDK.
/// </summary>
public sealed class TracekitConfig
{
    /// <summary>
    /// Gets the API key for authentication with TraceKit services.
    /// </summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// Gets the name of the service being instrumented.
    /// </summary>
    public required string ServiceName { get; init; }

    /// <summary>
    /// Gets the base endpoint URL. Can be a hostname or full URL.
    /// Default: "app.tracekit.dev"
    /// </summary>
    public string Endpoint { get; init; } = "app.tracekit.dev";

    /// <summary>
    /// Gets whether to use SSL/HTTPS. Default: true.
    /// Ignored if Endpoint already contains a scheme.
    /// </summary>
    public bool UseSSL { get; init; } = true;

    /// <summary>
    /// Gets the deployment environment (e.g., "production", "staging", "development").
    /// Default: "production"
    /// </summary>
    public string Environment { get; init; } = "production";

    /// <summary>
    /// Gets the service version. Default: "1.0.0"
    /// </summary>
    public string ServiceVersion { get; init; } = "1.0.0";

    /// <summary>
    /// Gets whether code monitoring (snapshots) is enabled. Default: true
    /// </summary>
    public bool EnableCodeMonitoring { get; init; } = true;

    /// <summary>
    /// Gets the code monitoring poll interval in seconds. Default: 30
    /// </summary>
    public int CodeMonitoringPollIntervalSeconds { get; init; } = 30;

    /// <summary>
    /// Gets the port for local UI auto-detection. Default: 9999
    /// </summary>
    public int LocalUIPort { get; init; } = 9999;

    /// <summary>
    /// Gets the sampling rate (0.0 to 1.0). Default: 1.0 (100%)
    /// </summary>
    public double SamplingRate { get; init; } = 1.0;

    /// <summary>
    /// Creates a new builder for constructing TracekitConfig instances.
    /// </summary>
    public static Builder CreateBuilder() => new Builder();

    /// <summary>
    /// Builder for creating TracekitConfig instances with fluent API.
    /// </summary>
    public sealed class Builder
    {
        private string? _apiKey;
        private string? _serviceName;
        private string _endpoint = "app.tracekit.dev";
        private bool _useSSL = true;
        private string _environment = "production";
        private string _serviceVersion = "1.0.0";
        private bool _enableCodeMonitoring = true;
        private int _codeMonitoringPollIntervalSeconds = 30;
        private int _localUIPort = 9999;
        private double _samplingRate = 1.0;

        internal Builder() { }

        public Builder WithApiKey(string apiKey)
        {
            _apiKey = apiKey;
            return this;
        }

        public Builder WithServiceName(string serviceName)
        {
            _serviceName = serviceName;
            return this;
        }

        public Builder WithEndpoint(string endpoint)
        {
            _endpoint = endpoint;
            return this;
        }

        public Builder WithUseSSL(bool useSSL)
        {
            _useSSL = useSSL;
            return this;
        }

        public Builder WithEnvironment(string environment)
        {
            _environment = environment;
            return this;
        }

        public Builder WithServiceVersion(string serviceVersion)
        {
            _serviceVersion = serviceVersion;
            return this;
        }

        public Builder WithEnableCodeMonitoring(bool enable)
        {
            _enableCodeMonitoring = enable;
            return this;
        }

        public Builder WithCodeMonitoringPollInterval(int seconds)
        {
            _codeMonitoringPollIntervalSeconds = seconds;
            return this;
        }

        public Builder WithLocalUIPort(int port)
        {
            _localUIPort = port;
            return this;
        }

        public Builder WithSamplingRate(double rate)
        {
            if (rate < 0.0 || rate > 1.0)
                throw new ArgumentOutOfRangeException(nameof(rate), "Sampling rate must be between 0.0 and 1.0");

            _samplingRate = rate;
            return this;
        }

        public TracekitConfig Build()
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("ApiKey is required");

            if (string.IsNullOrWhiteSpace(_serviceName))
                throw new InvalidOperationException("ServiceName is required");

            return new TracekitConfig
            {
                ApiKey = _apiKey,
                ServiceName = _serviceName,
                Endpoint = _endpoint,
                UseSSL = _useSSL,
                Environment = _environment,
                ServiceVersion = _serviceVersion,
                EnableCodeMonitoring = _enableCodeMonitoring,
                CodeMonitoringPollIntervalSeconds = _codeMonitoringPollIntervalSeconds,
                LocalUIPort = _localUIPort,
                SamplingRate = _samplingRate
            };
        }
    }
}
