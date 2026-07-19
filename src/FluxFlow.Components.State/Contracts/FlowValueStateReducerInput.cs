using System.Collections.Immutable;
using FluxFlow.Data;

namespace FluxFlow.Components.State.Contracts;

public sealed record FlowValueStateReducerInput
{
    private string _key = string.Empty;
    private FlowValue _input = FlowValue.Null;
    private ImmutableDictionary<string, FlowValue> _variables =
        ImmutableDictionary.Create<string, FlowValue>(StringComparer.Ordinal);

    public required string Key
    {
        get => _key;
        init => _key = StateContractNormalization.NormalizeRequired(value);
    }

    public FlowValue Input
    {
        get => _input;
        init => _input = value ?? FlowValue.Null;
    }

    public FlowValue? InitialState { get; init; }

    public IReadOnlyDictionary<string, FlowValue> Variables
    {
        get => _variables;
        init => _variables = value is null
            ? ImmutableDictionary.Create<string, FlowValue>(StringComparer.Ordinal)
            : value.ToImmutableDictionary(
                item => item.Key,
                item => item.Value ?? FlowValue.Null,
                StringComparer.Ordinal);
    }

    public StateReducerOperation Operation { get; init; } = StateReducerOperation.Reduce;
}
