using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Mapping;

public sealed class MappingComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        new()
        {
            Type = MappingComponentTypes.Mapper,
            DisplayName = "Mapper",
            Category = "Mapping",
            Summary = "Maps one input type into a configured output type using an expression.",
            IconKey = "mapper",
            PreferredNodeName = "mapper",
            SuggestedEditorWidth = 560,
            Options =
            [
                Text("inputType", "Input type", "object"),
                Text("outputType", "Output type", "object"),
                Text("engine", "Engine", "jsonata"),
                new()
                {
                    Name = "expression",
                    Kind = OptionValueKind.Expression,
                    DisplayName = "Expression",
                    IsRequired = true
                },
                Number("boundedCapacity", "Capacity", 128, 1)
            ],
            Ports =
            [
                Port(MappingComponentPorts.Input, PortDirection.Input, "Configured input type", true),
                Port(MappingComponentPorts.Output, PortDirection.Output, "Configured output type", true, 1),
                Port(MappingComponentPorts.Errors, PortDirection.Output, "FlowError", false, 2)
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
