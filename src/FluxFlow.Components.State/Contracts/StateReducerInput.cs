namespace FluxFlow.Components.State.Contracts;

public sealed record StateReducerInput
{
    private string _key = string.Empty;
    private Dictionary<string, object?> _variables = new(StringComparer.Ordinal);

    public required string Key
    {
        get => _key;
        init => _key = StateContractNormalization.NormalizeRequired(value);
    }

    public object? Input { get; init; }
    public object? InitialState { get; init; }

    public Dictionary<string, object?> Variables
    {
        get => _variables;
        init => _variables = StateContractNormalization.CopyVariables(value);
    }

    public StateReducerOperation Operation { get; init; } = StateReducerOperation.Reduce;
}
