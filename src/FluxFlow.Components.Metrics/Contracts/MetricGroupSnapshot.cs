namespace FluxFlow.Components.Metrics.Contracts;

public sealed record MetricGroupSnapshot
{
    public required string Group { get; init; }
    public required long Count { get; init; }
    public long ValueCount { get; init; }
    public double? TotalValue { get; init; }
    public double? AverageValue { get; init; }
    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }
    public required double CurrentRate { get; init; }
    public long? TotalSize { get; init; }
    public required DateTimeOffset LatestTimestamp { get; init; }
}
