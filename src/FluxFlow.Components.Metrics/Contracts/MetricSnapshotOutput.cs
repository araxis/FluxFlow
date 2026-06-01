namespace FluxFlow.Components.Metrics.Contracts;

public sealed record MetricSnapshotOutput
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? Name { get; init; }
    public string? Unit { get; init; }
    public long SampleCount { get; init; }
    public long ValueCount { get; init; }
    public double? TotalValue { get; init; }
    public double? AverageValue { get; init; }
    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }
    public double CurrentRate { get; init; }
    public double AverageRate { get; init; }
    public long? TotalSize { get; init; }
    public MetricSampleInput? Latest { get; init; }
    public Dictionary<string, MetricGroupSnapshot> Groups { get; init; } = [];
}
