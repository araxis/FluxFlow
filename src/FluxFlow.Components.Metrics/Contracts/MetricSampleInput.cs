namespace FluxFlow.Components.Metrics.Contracts;

public sealed record MetricSampleInput
{
    public DateTimeOffset? Timestamp { get; init; }
    public string? Name { get; init; }
    public string? Group { get; init; }
    public double? Value { get; init; }
    public string? Unit { get; init; }
    public long? Size { get; init; }
    public Dictionary<string, string> Tags { get; init; } = [];
}
