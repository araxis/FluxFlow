using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Sources;

public sealed class SourcesComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        Source(SourcesComponentTypes.Generated, "Generated Source", "generatedSource",
            "Emits configured values from a generated source definition.",
            [
                Text("name", "Name", defaultValue: "generated"),
                Text("outputType", "Output type", defaultValue: "object"),
                new()
                {
                    Name = "items",
                    Kind = OptionValueKind.Json,
                    DisplayName = "Items",
                    IsRequired = true,
                    DefaultValue = "[]"
                },
                Boolean("loop", "Loop", false),
                Number("maxItems", "Max items", null, 1),
                Number("initialDelayMilliseconds", "Initial delay ms", 0, 0),
                Number("intervalMilliseconds", "Interval ms", 0, 0),
                Capacity()
            ],
            "Configured output type"),
        Source(SourcesComponentTypes.Sequence, "Sequence Source", "sequenceSource",
            "Emits configured values in sequence order.",
            [
                Text("name", "Name", defaultValue: "sequence"),
                Number("start", "Start", 1, null),
                Number("step", "Step", 1, null),
                Number("count", "Count", 1, 1),
                Number("initialDelayMilliseconds", "Initial delay ms", 0, 0),
                Number("intervalMilliseconds", "Interval ms", 0, 0),
                Capacity()
            ],
            "long")
    ];

    private static ComponentDesignMetadata Source(
        NodeType type,
        string displayName,
        string preferredNodeName,
        string summary,
        IReadOnlyList<OptionDesignMetadata> options,
        string outputType) => new()
        {
            Type = type,
            DisplayName = displayName,
            Category = "Sources",
            Summary = summary,
            IconKey = "source",
            PreferredNodeName = preferredNodeName,
            SuggestedEditorWidth = 480,
            Options = options,
            Ports =
            [
                Port(SourcesComponentPorts.Output, PortDirection.Output, outputType, true),
                Port(SourcesComponentPorts.Errors, PortDirection.Output, "FlowError", false, 1)
            ]
        };

    private static OptionDesignMetadata Text(
        string name,
        string displayName,
        string? helperText = null,
        bool required = false,
        object? defaultValue = null) => new()
        {
            Name = name,
            Kind = OptionValueKind.Text,
            DisplayName = displayName,
            HelperText = helperText,
            IsRequired = required,
            DefaultValue = defaultValue
        };

    private static OptionDesignMetadata Number(string name, string displayName, object? defaultValue, double? min) => new()
    {
        Name = name,
        Kind = OptionValueKind.Number,
        DisplayName = displayName,
        DefaultValue = defaultValue,
        Min = min
    };

    private static OptionDesignMetadata Boolean(string name, string displayName, bool defaultValue) => new()
    {
        Name = name,
        Kind = OptionValueKind.Boolean,
        DisplayName = displayName,
        DefaultValue = defaultValue
    };

    private static OptionDesignMetadata Capacity() => Number("boundedCapacity", "Capacity", 128, 1);

    private static PortDesignMetadata Port(string name, PortDirection direction, string valueType, bool primary, int order = 0) => new()
    {
        Name = new PortName(name),
        Direction = direction,
        ValueType = valueType,
        IsPrimary = primary,
        Order = order
    };
}
