namespace FluxFlow.Components.Control.Contracts;

public sealed record ControlAssertionResult
{
    public required string Name { get; init; }
    public string? ExpressionId { get; init; }
    public string? ExpressionName { get; init; }
    public required string Expression { get; init; }
    public required string InputType { get; init; }
    public required bool Passed { get; init; }
    public object? Value { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset EvaluatedAt { get; init; } = DateTimeOffset.UtcNow;
}
