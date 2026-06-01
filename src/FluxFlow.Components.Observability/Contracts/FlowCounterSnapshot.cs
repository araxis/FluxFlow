namespace FluxFlow.Components.Observability.Contracts;

public sealed record FlowCounterSnapshot
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Name { get; init; }
    public required string InputType { get; init; }
    public required long Count { get; init; }
    public required long RejectedCount { get; init; }
    public required DateTimeOffset LastObservedAt { get; init; }
}
