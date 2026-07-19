using FluxFlow.Data;

namespace FluxFlow.Components.State.Contracts;

public sealed record FlowValueStateReducerResult
{
    private string _key = string.Empty;
    private FlowValue _previousState = FlowValue.Null;
    private FlowValue _input = FlowValue.Null;
    private FlowValue _newState = FlowValue.Null;

    public required string Key
    {
        get => _key;
        init => _key = StateContractNormalization.NormalizeRequired(value);
    }

    public FlowValue PreviousState
    {
        get => _previousState;
        init => _previousState = value ?? FlowValue.Null;
    }

    public FlowValue Input
    {
        get => _input;
        init => _input = value ?? FlowValue.Null;
    }

    public FlowValue NewState
    {
        get => _newState;
        init => _newState = value ?? FlowValue.Null;
    }

    public StateReducerOperation Operation { get; init; }

    public long Version { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
