namespace FluxFlow.Components.State.Contracts;

public sealed record StateReducerResult
{
    public required string Key { get; init; }
    public object? PreviousState { get; init; }
    public object? Input { get; init; }
    public object? NewState { get; init; }
    public long Version { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
