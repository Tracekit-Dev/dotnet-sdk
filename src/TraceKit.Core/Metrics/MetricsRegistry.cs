using System.Collections.Concurrent;

namespace TraceKit.Core.Metrics;

/// <summary>
/// Registry for managing metrics and exporting them to TraceKit.
/// Implements automatic buffering and periodic export (100 metrics or 10 seconds).
/// </summary>
public sealed class MetricsRegistry : IDisposable
{
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _serviceName;
    private readonly MetricsExporter _exporter;
    private readonly ConcurrentBag<MetricDataPoint> _buffer = new();
    private readonly Timer _flushTimer;
    private readonly object _flushLock = new object();
    private bool _disposed;

    private const int MaxBufferSize = 100;
    private const int FlushIntervalSeconds = 10;

    public MetricsRegistry(string endpoint, string apiKey, string serviceName)
    {
        _endpoint = endpoint;
        _apiKey = apiKey;
        _serviceName = serviceName;
        _exporter = new MetricsExporter(endpoint, apiKey, serviceName);

        // Start periodic flush timer
        _flushTimer = new Timer(
            _ => FlushAsync().GetAwaiter().GetResult(),
            null,
            TimeSpan.FromSeconds(FlushIntervalSeconds),
            TimeSpan.FromSeconds(FlushIntervalSeconds)
        );
    }

    internal void RecordMetric(string name, string type, double value, Dictionary<string, string> tags)
    {
        var dataPoint = new MetricDataPoint
        {
            Name = name,
            Type = type,
            Value = value,
            Tags = new Dictionary<string, string>(tags),
            TimestampNanos = DateTimeOffset.UtcNow.ToUnixTimeNanoseconds()
        };

        _buffer.Add(dataPoint);

        // Auto-flush if buffer is full
        if (_buffer.Count >= MaxBufferSize)
        {
            _ = FlushAsync();
        }
    }

    private async Task FlushAsync()
    {
        if (_buffer.IsEmpty) return;

        lock (_flushLock)
        {
            if (_buffer.IsEmpty) return;

            var dataPoints = new List<MetricDataPoint>();
            while (_buffer.TryTake(out var dp))
            {
                dataPoints.Add(dp);
            }

            if (dataPoints.Count > 0)
            {
                try
                {
                    _exporter.Export(dataPoints);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to export metrics: {ex.Message}");
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _flushTimer?.Dispose();
        FlushAsync().GetAwaiter().GetResult();
    }
}
