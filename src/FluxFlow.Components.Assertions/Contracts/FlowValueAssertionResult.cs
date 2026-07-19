using FluxFlow.Data;

namespace FluxFlow.Components.Assertions.Contracts;

/// <summary>
/// Transport-neutral outcome of evaluating an immutable workflow value.
/// </summary>
public sealed record FlowValueAssertionResult
{
    public required DateTimeOffset EvaluatedAt { get; init; }

    public required FlowValue Input { get; init; }

    public required bool Passed { get; init; }

    public required string Description { get; init; }

    public required string Message { get; init; }

    public required string Expression { get; init; }

    public string? ExpressionId { get; init; }

    public string? ExpressionName { get; init; }

    public required string EngineName { get; init; }

    public required string InputType { get; init; }
}
