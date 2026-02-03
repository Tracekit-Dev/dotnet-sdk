namespace TraceKit.Core.Metrics;

internal sealed class MetricDataPoint
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required double Value { get; init; }
    public required Dictionary<string, string> Tags { get; init; }
    public required long TimestampNanos { get; init; }
}
