using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Sources;

public sealed class SourcesComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        Metadata(SourcesComponentTypes.Generated, "Generated Source", "generatedSource",
            "Emits configured values from a generated source definition."),
        Metadata(SourcesComponentTypes.Sequence, "Sequence Source", "sequenceSource",
            "Emits configured values in sequence order.")
    ];

    private static ComponentDesignMetadata Metadata(NodeType type, string displayName, string preferredName, string summary) => new()
    {
        Type = type,
        DisplayName = displayName,
        Category = "Sources",
        Summary = summary,
        IconKey = "source",
        PreferredNodeName = preferredName,
        SuggestedEditorWidth = 480,
        Options =
        [
            new()
            {
                Name = "items",
                Kind = OptionValueKind.Json,
                DisplayName = "Items"
            },
            new()
            {
                Name = "boundedCapacity",
                Kind = OptionValueKind.Number,
                DisplayName = "Capacity",
                DefaultValue = 128,
                Min = 1
            }
        ],
        Ports =
        [
            Port(SourcesComponentPorts.Output, PortDirection.Output, "Configured output type", true),
            Port(SourcesComponentPorts.Errors, PortDirection.Output, "FlowError", false, 1)
        ]
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
