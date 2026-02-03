namespace TraceKit.Core.Metrics;

/// <summary>
/// Gauge metric - point-in-time value that can increase or decrease.
/// Used for tracking current values like active connections, memory usage, etc.
/// </summary>
public sealed class Gauge
{
    private readonly string _name;
    private readonly Dictionary<string, string> _tags;
    private readonly MetricsRegistry _registry;
    private double _value;
    private readonly object _lock = new object();

    internal Gauge(string name, Dictionary<string, string>? tags, MetricsRegistry registry)
    {
        _name = name;
        _tags = tags ?? new Dictionary<string, string>();
        _registry = registry;
    }

    /// <summary>
    /// Sets the gauge to a specific value.
    /// </summary>
    public void Set(double value)
    {
        lock (_lock)
        {
            _value = value;
            _registry.RecordMetric(_name, "gauge", _value, _tags);
        }
    }

    /// <summary>
    /// Increments the gauge by 1.
    /// </summary>
    public void Inc()
    {
        lock (_lock)
        {
            _value++;
            _registry.RecordMetric(_name, "gauge", _value, _tags);
        }
    }

    /// <summary>
    /// Decrements the gauge by 1.
    /// </summary>
    public void Dec()
    {
        lock (_lock)
        {
            _value--;
            _registry.RecordMetric(_name, "gauge", _value, _tags);
        }
    }
}
