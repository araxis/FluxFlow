using System.Collections.Immutable;

namespace FluxFlow.Components.State.Contracts;

public sealed record StateReducerInput<T>
{
    private string _key = string.Empty;
    private T? _initialState;
    private bool _hasInitialState;
    private ImmutableDictionary<string, object?> _variables =
        ImmutableDictionary.Create<string, object?>(StringComparer.Ordinal);

    public required string Key
    {
        get => _key;
        init => _key = StateContractNormalization.NormalizeRequired(value);
    }

    public T? Input { get; init; }

    public T? InitialState
    {
        get => _initialState;
        init
        {
            _initialState = value;
            _hasInitialState = true;
        }
    }

    internal bool HasInitialState => _hasInitialState;

    public IReadOnlyDictionary<string, object?> Variables
    {
        get => _variables;
        init => _variables = value is null
            ? ImmutableDictionary.Create<string, object?>(StringComparer.Ordinal)
            : value.ToImmutableDictionary(StringComparer.Ordinal);
    }

    public StateReducerOperation Operation { get; init; } = StateReducerOperation.Reduce;
}
