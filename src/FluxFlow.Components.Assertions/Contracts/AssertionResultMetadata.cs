namespace FluxFlow.Components.Assertions.Contracts;

public sealed record AssertionResultMetadata
{
    public required string EffectiveDescription { get; init; }
    public required string Expression { get; init; }
    public string? ExpressionId { get; init; }
    public string? ExpressionName { get; init; }
    public required string EngineName { get; init; }
    public required string InputType { get; init; }
    public required string EffectiveFailureMessage { get; init; }
    public required bool EmitPassedInput { get; init; }
    public required bool EmitFailedInput { get; init; }
    public required int BoundedCapacity { get; init; }
}
