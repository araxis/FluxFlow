namespace FluxFlow.Components.State.Options;

public sealed record StateReducerOptions
{
    public string? Engine { get; init; }
    public string? KeyExpression { get; init; }
    public required string Reducer { get; init; }
    public string? ExpressionId { get; init; }
    public string? ExpressionName { get; init; }
    public object? InitialState { get; init; }
    public int BoundedCapacity { get; init; } = 128;
    public int MaxKeys { get; init; } = 1024;
}
