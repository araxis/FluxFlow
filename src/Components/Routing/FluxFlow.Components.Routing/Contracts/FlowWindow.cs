namespace FluxFlow.Components.Routing.Contracts;

public sealed record FlowWindow<TInput>
{
    public required long Sequence { get; init; }
    public required IReadOnlyList<TInput> Items { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset EmittedAt { get; init; }
    public required FlowWindowEmitReason Reason { get; init; }
    public int Count => Items.Count;
    public TimeSpan Duration => EmittedAt - StartedAt;
}
