namespace FluxFlow.Components.Observability.Contracts;

public sealed record FlowMetricSnapshot
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Name { get; init; }
    public required string InputType { get; init; }
    public required long Count { get; init; }
    public required DateTimeOffset LastObservedAt { get; init; }
    public required double CurrentRatePerSecond { get; init; }
    public required double AverageRatePerSecond { get; init; }
    public double? LastSize { get; init; }
    public double? TotalSize { get; init; }
    public double? AverageSize { get; init; }
}
