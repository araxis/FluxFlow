namespace FluxFlow.Components.State.Contracts;

public sealed record StateReducerResult<T>
{
    private string _key = string.Empty;

    public required string Key
    {
        get => _key;
        init => _key = StateContractNormalization.NormalizeRequired(value);
    }

    public T? PreviousState { get; init; }
    public T? Input { get; init; }
    public T? NewState { get; init; }
    public StateReducerOperation Operation { get; init; }
    public long Version { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
