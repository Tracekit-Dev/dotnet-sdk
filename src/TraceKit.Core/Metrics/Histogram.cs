namespace TraceKit.Core.Metrics;

/// <summary>
/// Histogram metric - records distribution of values.
/// Used for tracking request durations, payload sizes, etc.
/// </summary>
public sealed class Histogram
{
    private readonly string _name;
    private readonly Dictionary<string, string> _tags;
    private readonly MetricsRegistry _registry;

    internal Histogram(string name, Dictionary<string, string>? tags, MetricsRegistry registry)
    {
        _name = name;
        _tags = tags ?? new Dictionary<string, string>();
        _registry = registry;
    }

    /// <summary>
    /// Records a value in the histogram.
    /// </summary>
    public void Record(double value)
    {
        _registry.RecordMetric(_name, "histogram", value, _tags);
    }
}
