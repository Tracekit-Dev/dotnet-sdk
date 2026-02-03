using System.Runtime.CompilerServices;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TraceKit.Core.LocalUI;
using TraceKit.Core.Metrics;
using TraceKit.Core.Snapshots;

namespace TraceKit.Core;

/// <summary>
/// Main SDK class for TraceKit .NET SDK with OpenTelemetry integration.
/// Provides distributed tracing, metrics collection, and code monitoring capabilities.
/// </summary>
public sealed class TracekitSDK : IDisposable
{
    private readonly TracekitConfig _config;
    private readonly TracerProvider? _tracerProvider;
    private readonly MetricsRegistry _metricsRegistry;
    private readonly SnapshotClient? _snapshotClient;
    private bool _disposed;

    private TracekitSDK(TracekitConfig config)
    {
        _config = config;

        Console.WriteLine($"Initializing TraceKit SDK v0.1.0 for service: {config.ServiceName}, environment: {config.Environment}");

        // Auto-detect local UI
        var localUIDetector = new LocalUIDetector(config.LocalUIPort);
        var localEndpoint = localUIDetector.GetLocalUIEndpoint();
        if (localEndpoint != null)
        {
            Console.WriteLine($"Local UI detected at {localEndpoint}");
        }

        // Resolve endpoints
        var tracesEndpoint = EndpointResolver.ResolveEndpoint(config.Endpoint, "/v1/traces", config.UseSSL);
        var metricsEndpoint = EndpointResolver.ResolveEndpoint(config.Endpoint, "/v1/metrics", config.UseSSL);
        var snapshotBaseUrl = EndpointResolver.ResolveEndpoint(config.Endpoint, "", config.UseSSL);

        // Initialize OpenTelemetry tracer
        _tracerProvider = CreateTracerProvider(config, tracesEndpoint);

        // Initialize metrics registry
        _metricsRegistry = new MetricsRegistry(metricsEndpoint, config.ApiKey, config.ServiceName);

        // Initialize snapshot client if code monitoring is enabled
        if (config.EnableCodeMonitoring)
        {
            _snapshotClient = new SnapshotClient(
                config.ApiKey,
                snapshotBaseUrl,
                config.ServiceName,
                config.CodeMonitoringPollIntervalSeconds
            );
            Console.WriteLine("Code monitoring enabled - Snapshot client started");
        }

        Console.WriteLine($"TraceKit SDK initialized successfully. Traces: {tracesEndpoint}, Metrics: {metricsEndpoint}");
    }

    private static TracerProvider CreateTracerProvider(TracekitConfig config, string tracesEndpoint)
    {
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: config.ServiceName, serviceVersion: config.ServiceVersion)
            .AddAttributes(new[]
            {
                new KeyValuePair<string, object>("deployment.environment", config.Environment)
            });

        return Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddSource(config.ServiceName)
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(tracesEndpoint);
                options.Headers = $"X-API-Key={config.ApiKey}";
                options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
            })
            .SetSampler(new TraceIdRatioBasedSampler(config.SamplingRate))
            .Build();
    }

    /// <summary>
    /// Creates a new TracekitSDK instance with the given configuration.
    /// </summary>
    public static TracekitSDK Create(TracekitConfig config)
    {
        return new TracekitSDK(config);
    }

    /// <summary>
    /// Gets the service name from the configuration.
    /// </summary>
    public string ServiceName => _config.ServiceName;

    /// <summary>
    /// Creates a Counter metric for tracking monotonically increasing values.
    /// </summary>
    /// <param name="name">Metric name (e.g., "http.requests.total")</param>
    /// <param name="tags">Optional tags for the metric</param>
    public Counter Counter(string name, Dictionary<string, string>? tags = null)
    {
        return new Counter(name, tags, _metricsRegistry);
    }

    /// <summary>
    /// Creates a Gauge metric for tracking point-in-time values.
    /// </summary>
    /// <param name="name">Metric name (e.g., "http.requests.active")</param>
    /// <param name="tags">Optional tags for the metric</param>
    public Gauge Gauge(string name, Dictionary<string, string>? tags = null)
    {
        return new Gauge(name, tags, _metricsRegistry);
    }

    /// <summary>
    /// Creates a Histogram metric for tracking value distributions.
    /// </summary>
    /// <param name="name">Metric name (e.g., "http.request.duration")</param>
    /// <param name="tags">Optional tags for the metric</param>
    public Histogram Histogram(string name, Dictionary<string, string>? tags = null)
    {
        return new Histogram(name, tags, _metricsRegistry);
    }

    /// <summary>
    /// Captures a snapshot of local variables at the current code location.
    /// Only active if code monitoring is enabled and there's an active breakpoint.
    /// </summary>
    /// <param name="label">Stable identifier for this snapshot location</param>
    /// <param name="variables">Variables to capture in the snapshot</param>
    public void CaptureSnapshot(string label, Dictionary<string, object> variables,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string functionName = "")
    {
        _snapshotClient?.CaptureSnapshot(label, variables, filePath, lineNumber, functionName);
    }

    /// <summary>
    /// Disposes the SDK and flushes any pending data.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _tracerProvider?.Dispose();
        _metricsRegistry?.Dispose();
        _snapshotClient?.Dispose();

        Console.WriteLine("TraceKit SDK shutdown complete");
    }
}
