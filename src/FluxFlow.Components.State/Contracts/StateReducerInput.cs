namespace FluxFlow.Components.State.Contracts;

public sealed record StateReducerInput
{
    public required string Key { get; init; }
    public object? Input { get; init; }
    public object? InitialState { get; init; }
    public Dictionary<string, object?> Variables { get; init; } = [];
    public StateReducerOperation Operation { get; init; } = StateReducerOperation.Reduce;
}
