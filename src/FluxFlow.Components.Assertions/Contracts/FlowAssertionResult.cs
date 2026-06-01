namespace FluxFlow.Components.Assertions.Contracts;

public sealed record FlowAssertionResult
{
    public required string Description { get; init; }
    public string? ExpressionId { get; init; }
    public string? ExpressionName { get; init; }
    public required string Expression { get; init; }
    public required string InputType { get; init; }
    public required FlowAssertionStatus Status { get; init; }
    public bool Passed => Status == FlowAssertionStatus.Passed;
    public required string Message { get; init; }
    public object? Value { get; init; }
    public AssertionFailure? Failure { get; init; }
    public DateTimeOffset EvaluatedAt { get; init; } = DateTimeOffset.UtcNow;
}
