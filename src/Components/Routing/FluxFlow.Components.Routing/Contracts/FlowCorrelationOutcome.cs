namespace FluxFlow.Components.Routing.Contracts;

public abstract record FlowCorrelationOutcome<TInput>;

public sealed record FlowCorrelationMatchedOutcome<TInput>
    : FlowCorrelationOutcome<TInput>
{
    public required FlowCorrelationMatch<TInput> Match { get; init; }
}

public sealed record FlowCorrelationTimedOutOutcome<TInput>
    : FlowCorrelationOutcome<TInput>
{
    public required FlowCorrelationTimeout<TInput> Timeout { get; init; }
}
