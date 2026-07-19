namespace FluxFlow.Components.Routing.Contracts;

public abstract record FlowJoinOutcome<TLeft, TRight>;

public sealed record FlowJoinMatchedOutcome<TLeft, TRight>
    : FlowJoinOutcome<TLeft, TRight>
{
    public required FlowJoinResult<TLeft, TRight> Match { get; init; }
}

public sealed record FlowJoinTimedOutOutcome<TLeft, TRight>
    : FlowJoinOutcome<TLeft, TRight>
{
    public required FlowJoinTimeout<TLeft, TRight> Timeout { get; init; }
}
