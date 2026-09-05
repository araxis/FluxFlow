namespace FluxFlow.Components.Routing.Contracts;

public sealed record FlowCorrelationTimeout<TInput>
{
    public required string Key { get; init; }
    public required string Side { get; init; }
    public required TInput Value { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public required DateTimeOffset TimedOutAt { get; init; }
    public required TimeSpan Timeout { get; init; }
}
