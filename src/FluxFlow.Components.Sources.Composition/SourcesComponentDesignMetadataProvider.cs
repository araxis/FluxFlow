using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Options;

namespace FluxFlow.Components.Sources.Composition;

public sealed class SourcesComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        =>
        [
            CreateGeneratedMetadata(),
            CreateSequenceMetadata()
        ];

    private static ComponentDesignMetadata CreateGeneratedMetadata() => new()
    {
        Type = new ComponentType(SourcesCompositionNodeTypes.Generated),
        DisplayName = "Generated Source",
        Category = "Sources",
        Summary = "Emits inline configured items as typed source messages.",
        IconKey = "list-plus",
        PreferredNodeName = "generated",
        SuggestedEditorWidth = 440,
        Options =
        [
            NameOption(GeneratedSourceOptions.DefaultName),
            new OptionDesignMetadata
            {
                Name = "outputType",
                Kind = OptionValueKind.Text,
                DisplayName = "Output Type",
                DefaultValue = GeneratedSourceOptions.ObjectTypeName,
                HelperText = "Diagnostic output type metadata; CLR output type comes from the closed registration."
            },
            new OptionDesignMetadata
            {
                Name = "items",
                Kind = OptionValueKind.Json,
                DisplayName = "Items",
                HelperText = "Inline array of payloads deserialized into the closed generated output type."
            },
            new OptionDesignMetadata
            {
                Name = "loop",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Loop",
                DefaultValue = false,
                HelperText = "Repeat configured items until maxItems is reached."
            },
            MaxItemsOption(),
            MillisecondsOption(
                "initialDelayMilliseconds",
                "Initial Delay Milliseconds",
                "Delay before the first item is emitted."),
            MillisecondsOption(
                "intervalMilliseconds",
                "Interval Milliseconds",
                "Delay between emitted items."),
            BoundedCapacityOption()
        ],
        Ports =
        [
            OutputPort("TOutput", "Generated source item.", isPrimary: true)
        ]
    };

    private static ComponentDesignMetadata CreateSequenceMetadata() => new()
    {
        Type = new ComponentType(SourcesCompositionNodeTypes.Sequence),
        DisplayName = "Sequence Source",
        Category = "Sources",
        Summary = "Emits numeric sequence items as source messages.",
        IconKey = "list-ordered",
        PreferredNodeName = "sequence",
        SuggestedEditorWidth = 420,
        Options =
        [
            NameOption(SequenceSourceOptions.DefaultName),
            new OptionDesignMetadata
            {
                Name = "start",
                Kind = OptionValueKind.Number,
                DisplayName = "Start",
                DefaultValue = 1,
                HelperText = "First numeric value emitted."
            },
            new OptionDesignMetadata
            {
                Name = "step",
                Kind = OptionValueKind.Number,
                DisplayName = "Step",
                DefaultValue = 1,
                HelperText = "Amount added for each item; cannot be zero."
            },
            new OptionDesignMetadata
            {
                Name = "count",
                Kind = OptionValueKind.Number,
                DisplayName = "Count",
                DefaultValue = 1,
                Min = 1,
                HelperText = "Number of sequence items to emit."
            },
            MillisecondsOption(
                "initialDelayMilliseconds",
                "Initial Delay Milliseconds",
                "Delay before the first item is emitted."),
            MillisecondsOption(
                "intervalMilliseconds",
                "Interval Milliseconds",
                "Delay between emitted items."),
            BoundedCapacityOption()
        ],
        Ports =
        [
            OutputPort(nameof(SourceSequenceItem), "Sequence source item.", isPrimary: true)
        ]
    };

    private static OptionDesignMetadata NameOption(string defaultValue) => new()
    {
        Name = "name",
        Kind = OptionValueKind.Text,
        DisplayName = "Name",
        DefaultValue = defaultValue,
        HelperText = "Name emitted in source diagnostics and payloads."
    };

    private static OptionDesignMetadata MaxItemsOption() => new()
    {
        Name = "maxItems",
        Kind = OptionValueKind.Number,
        DisplayName = "Max Items",
        Min = 1,
        HelperText = "Optional maximum number of generated items to emit."
    };

    private static OptionDesignMetadata MillisecondsOption(
        string name,
        string displayName,
        string helperText) => new()
        {
            Name = name,
            Kind = OptionValueKind.Number,
            DisplayName = displayName,
            DefaultValue = 0,
            Min = 0,
            HelperText = helperText
        };

    private static OptionDesignMetadata BoundedCapacityOption() => new()
    {
        Name = "boundedCapacity",
        Kind = OptionValueKind.Number,
        DisplayName = "Bounded Capacity",
        DefaultValue = 128,
        Min = 1,
        HelperText = "Maximum queued source messages."
    };

    private static PortDesignMetadata OutputPort(
        string valueType,
        string summary,
        bool isPrimary) => new()
        {
            Name = new ComponentPortName(SourcesCompositionPortNames.Output),
            Direction = PortDirection.Output,
            DisplayName = "Output",
            Group = "Messages",
            Order = 0,
            Summary = summary,
            ValueType = valueType,
            IsPrimary = isPrimary
        };
}
