namespace FluxFlow.Components.Assertions.Contracts;

/// <summary>
/// Outcome of evaluating one typed workflow value.
/// </summary>
public sealed record AssertionResult<T>
{
    public required DateTimeOffset EvaluatedAt { get; init; }
    public required T Input { get; init; }
    public required bool Passed { get; init; }
    public required string Description { get; init; }
    public required string Message { get; init; }
    public required string Expression { get; init; }
    public string? ExpressionId { get; init; }
    public string? ExpressionName { get; init; }
    public required string EngineName { get; init; }
    public required string InputType { get; init; }
}
