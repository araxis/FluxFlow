namespace FluxFlow.Components.Routing.Contracts;

public sealed record FlowSwitchResult<TInput>
{
    public string? RouteKey { get; init; }
    public required bool Matched { get; init; }
    public string? DefaultRoute { get; init; }
    public string? ExpressionId { get; init; }
    public string? ExpressionName { get; init; }
    public required string Expression { get; init; }
    public required string InputType { get; init; }
    public TInput? Value { get; init; }
    public required DateTimeOffset EvaluatedAt { get; init; }
}
