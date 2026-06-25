namespace FluxFlow.Components.State.Contracts;

public sealed record StateReducerResult
{
    private string _key = string.Empty;

    public required string Key
    {
        get => _key;
        init => _key = StateContractNormalization.NormalizeRequired(value);
    }

    public object? PreviousState { get; init; }
    public object? Input { get; init; }
    public object? NewState { get; init; }
    public long Version { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
