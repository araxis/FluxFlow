namespace FluxFlow.Components.Routing.Contracts;

public sealed record FlowJoinTimeout<TLeft, TRight>
{
    public required string Key { get; init; }
    public required FlowJoinSide Side { get; init; }
    public TLeft? Left { get; init; }
    public TRight? Right { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public required DateTimeOffset TimedOutAt { get; init; }
    public required TimeSpan Timeout { get; init; }
}
