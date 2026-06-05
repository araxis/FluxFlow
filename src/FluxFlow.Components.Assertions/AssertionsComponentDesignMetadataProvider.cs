using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Assertions;

public sealed class AssertionsComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        new()
        {
            Type = AssertionsComponentTypes.Assert,
            DisplayName = "Assertion",
            Category = "Assertions",
            Summary = "Evaluates an input stream against an expected condition.",
            IconKey = "assertion",
            PreferredNodeName = "assertion",
            SuggestedEditorWidth = 520,
            Options =
            [
                Text("assertionName", "Assertion name", "assertion"),
                Text("inputType", "Input type", "object"),
                Expression("expression", "Expression", "input != null"),
                Text("failureMessage", "Failure message", "Assertion failed."),
                Number("boundedCapacity", "Capacity", 128, 1)
            ],
            Ports =
            [
                Port(AssertionsComponentPorts.Input, PortDirection.Input, "Configured input type", true),
                Port(AssertionsComponentPorts.Result, PortDirection.Output, "FlowAssertionResult", true, 1),
                Port(AssertionsComponentPorts.Passed, PortDirection.Output, "Configured input type", false, 2),
                Port(AssertionsComponentPorts.Failed, PortDirection.Output, "Configured input type", false, 3),
                Port(AssertionsComponentPorts.Errors, PortDirection.Output, "FlowError", false, 4)
            ]
        }
    ];

    private static OptionDesignMetadata Text(string name, string displayName, object defaultValue) => new()
    {
        Name = name,
        Kind = OptionValueKind.Text,
        DisplayName = displayName,
        DefaultValue = defaultValue
    };

    private static OptionDesignMetadata Expression(string name, string displayName, object defaultValue) => new()
    {
        Name = name,
        Kind = OptionValueKind.Expression,
        DisplayName = displayName,
        DefaultValue = defaultValue,
        IsRequired = true
    };

    private static OptionDesignMetadata Number(string name, string displayName, object defaultValue, double min) => new()
    {
        Name = name,
        Kind = OptionValueKind.Number,
        DisplayName = displayName,
        DefaultValue = defaultValue,
        Min = min
    };

    private static PortDesignMetadata Port(
        string name,
        PortDirection direction,
        string valueType,
        bool primary,
        int order = 0) => new()
        {
            Name = new PortName(name),
            Direction = direction,
            ValueType = valueType,
            IsPrimary = primary,
            Order = order
        };
}
