using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.State;

public sealed class StateComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        new()
        {
            Type = StateComponentTypes.Reducer,
            DisplayName = "State Reducer",
            Category = "State",
            Summary = "Keeps keyed state from reducer inputs and emits updated state snapshots.",
            IconKey = "state",
            PreferredNodeName = "stateReducer",
            SuggestedEditorWidth = 560,
            Options =
            [
                Text("engine", "Engine", "jsonata"),
                Expression("keyExpression", "Key expression", "input.key"),
                Expression("reducer", "Reducer", "$merge([$state, $input])"),
                Text("expressionId", "Expression id"),
                Text("expressionName", "Expression name"),
                Json("initialState", "Initial state"),
                Number("boundedCapacity", "Capacity", 128, 1),
                Number("maxKeys", "Max keys", 1024, 1)
            ],
            Ports =
            [
                Port(StateComponentPorts.Input, PortDirection.Input, "StateReducerInput", true),
                Port(StateComponentPorts.Output, PortDirection.Output, "StateReducerResult", true, 1),
                Port(StateComponentPorts.Errors, PortDirection.Output, "FlowError", false, 2)
            ]
        }
    ];

    private static OptionDesignMetadata Text(string name, string displayName, object? defaultValue = null) => new()
    {
        Name = name,
        Kind = OptionValueKind.Text,
        DisplayName = displayName,
        DefaultValue = defaultValue
    };

    private static OptionDesignMetadata Expression(string name, string displayName, object? defaultValue = null) => new()
    {
        Name = name,
        Kind = OptionValueKind.Expression,
        DisplayName = displayName,
        DefaultValue = defaultValue,
        IsRequired = name == "reducer"
    };

    private static OptionDesignMetadata Json(string name, string displayName) => new()
    {
        Name = name,
        Kind = OptionValueKind.Json,
        DisplayName = displayName
    };

    private static OptionDesignMetadata Number(string name, string displayName, object defaultValue, double min) => new()
    {
        Name = name,
        Kind = OptionValueKind.Number,
        DisplayName = displayName,
        DefaultValue = defaultValue,
        Min = min
    };

    private static PortDesignMetadata Port(string name, PortDirection direction, string valueType, bool primary, int order = 0) => new()
    {
        Name = new PortName(name),
        Direction = direction,
        ValueType = valueType,
        IsPrimary = primary,
        Order = order
    };
}
