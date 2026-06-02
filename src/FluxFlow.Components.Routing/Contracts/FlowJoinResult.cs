namespace FluxFlow.Components.Routing.Contracts;

public sealed record FlowJoinResult<TLeft, TRight>
{
    public required string Key { get; init; }
    public required TLeft Left { get; init; }
    public required TRight Right { get; init; }
    public required DateTimeOffset LeftReceivedAt { get; init; }
    public required DateTimeOffset RightReceivedAt { get; init; }
    public required DateTimeOffset JoinedAt { get; init; }
    public TimeSpan Elapsed { get; init; }
}
