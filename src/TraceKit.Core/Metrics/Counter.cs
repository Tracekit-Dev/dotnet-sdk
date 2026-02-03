namespace TraceKit.Core.Metrics;

/// <summary>
/// Counter metric - monotonically increasing value.
/// Used for tracking totals like request counts, error counts, etc.
/// </summary>
public sealed class Counter
{
    private readonly string _name;
    private readonly Dictionary<string, string> _tags;
    private readonly MetricsRegistry _registry;
    private double _value;
    private readonly object _lock = new object();

    internal Counter(string name, Dictionary<string, string>? tags, MetricsRegistry registry)
    {
        _name = name;
        _tags = tags ?? new Dictionary<string, string>();
        _registry = registry;
    }

    /// <summary>
    /// Increments the counter by 1.
    /// </summary>
    public void Inc()
    {
        Add(1.0);
    }

    /// <summary>
    /// Adds a value to the counter.
    /// </summary>
    public void Add(double value)
    {
        if (value < 0)
            throw new ArgumentException("Counter values must be non-negative", nameof(value));

        lock (_lock)
        {
            _value += value;
            _registry.RecordMetric(_name, "counter", _value, _tags);
        }
    }
}
