namespace FluxFlow.Components.Routing.Contracts;

public sealed record FlowMergeItem<TInput>
{
    public required long Sequence { get; init; }
    public required string Source { get; init; }
    public required TInput Value { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
}
