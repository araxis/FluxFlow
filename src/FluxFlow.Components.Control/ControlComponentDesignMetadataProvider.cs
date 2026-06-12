using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Control;

public sealed class ControlComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        new()
        {
            Type = ControlComponentTypes.Filter,
            DisplayName = "Filter",
            Category = "Control",
            Summary = "Lets matching inputs continue downstream and drops the rest.",
            IconKey = "filter",
            PreferredNodeName = "filter",
            SuggestedEditorWidth = 480,
            Options = CommonOptions(),
            Ports =
            [
                Port(ControlComponentPorts.Input, PortDirection.Input, "Configured input type", true),
                Port(ControlComponentPorts.Output, PortDirection.Output, "Configured input type", true, 1),
                Port(ControlComponentPorts.Errors, PortDirection.Output, "FlowError", false, 2)
            ]
        },
        new()
        {
            Type = ControlComponentTypes.When,
            DisplayName = "When",
            Category = "Control",
            Summary = "Routes each input to true or false output branches.",
            IconKey = "route",
            PreferredNodeName = "when",
            SuggestedEditorWidth = 480,
            Options = CommonOptions(),
            Ports =
            [
                Port(ControlComponentPorts.Input, PortDirection.Input, "Configured input type", true),
                Port(ControlComponentPorts.WhenTrue, PortDirection.Output, "Configured input type", true, 1),
                Port(ControlComponentPorts.WhenFalse, PortDirection.Output, "Configured input type", false, 2),
                Port(ControlComponentPorts.Errors, PortDirection.Output, "FlowError", false, 3)
            ]
        }
    ];

    private static IReadOnlyList<OptionDesignMetadata> CommonOptions() =>
    [
        new()
        {
            Name = "inputType",
            Kind = OptionValueKind.Text,
            DisplayName = "Input type",
            DefaultValue = "object"
        },
        new()
        {
            Name = "expression",
            Kind = OptionValueKind.Expression,
            DisplayName = "Expression",
            IsRequired = true,
            DefaultValue = "true"
        },
        new()
        {
            Name = "boundedCapacity",
            Kind = OptionValueKind.Number,
            DisplayName = "Capacity",
            DefaultValue = 128,
            Min = 1
        }
    ];

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
