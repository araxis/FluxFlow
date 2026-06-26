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

    private static ComponentDesignMetadata CreateGeneratedMetadata()
        => CreateSourceMetadata(
            SourcesCompositionNodeTypes.Generated,
            "Generated Source",
            "Emits inline configured items as typed source messages.",
            "list-plus",
            "generated",
            suggestedEditorWidth: 440,
            builder =>
            {
                AddNameOption(builder, GeneratedSourceOptions.DefaultName);
                builder
                    .AddOption(
                        "outputType",
                        OptionValueKind.Text,
                        displayName: "Output Type",
                        helperText: "Diagnostic output type metadata; CLR output type comes from the closed registration.",
                        defaultValue: GeneratedSourceOptions.ObjectTypeName)
                    .AddOption(
                        "items",
                        OptionValueKind.Json,
                        displayName: "Items",
                        helperText: "Inline array of payloads deserialized into the closed generated output type.")
                    .AddOption(
                        "loop",
                        OptionValueKind.Boolean,
                        displayName: "Loop",
                        helperText: "Repeat configured items until maxItems is reached.",
                        defaultValue: false);
                AddMaxItemsOption(builder);
                AddMillisecondsOption(
                    builder,
                    "initialDelayMilliseconds",
                    "Initial Delay Milliseconds",
                    "Delay before the first item is emitted.");
                AddMillisecondsOption(
                    builder,
                    "intervalMilliseconds",
                    "Interval Milliseconds",
                    "Delay between emitted items.");
                AddBoundedCapacityOption(builder);
                AddOutputPort(builder, "TOutput", "Generated source item.");
            });

    private static ComponentDesignMetadata CreateSequenceMetadata()
        => CreateSourceMetadata(
            SourcesCompositionNodeTypes.Sequence,
            "Sequence Source",
            "Emits numeric sequence items as source messages.",
            "list-ordered",
            "sequence",
            suggestedEditorWidth: 420,
            builder =>
            {
                AddNameOption(builder, SequenceSourceOptions.DefaultName);
                builder
                    .AddOption(
                        "start",
                        OptionValueKind.Number,
                        displayName: "Start",
                        helperText: "First numeric value emitted.",
                        defaultValue: 1)
                    .AddOption(
                        "step",
                        OptionValueKind.Number,
                        displayName: "Step",
                        helperText: "Amount added for each item; cannot be zero.",
                        defaultValue: 1)
                    .AddOption(
                        "count",
                        OptionValueKind.Number,
                        displayName: "Count",
                        helperText: "Number of sequence items to emit.",
                        defaultValue: 1,
                        min: 1);
                AddMillisecondsOption(
                    builder,
                    "initialDelayMilliseconds",
                    "Initial Delay Milliseconds",
                    "Delay before the first item is emitted.");
                AddMillisecondsOption(
                    builder,
                    "intervalMilliseconds",
                    "Interval Milliseconds",
                    "Delay between emitted items.");
                AddBoundedCapacityOption(builder);
                AddOutputPort(builder, nameof(SourceSequenceItem), "Sequence source item.");
            });

    private static ComponentDesignMetadata CreateSourceMetadata(
        string type,
        string displayName,
        string summary,
        string iconKey,
        string preferredNodeName,
        int suggestedEditorWidth,
        Action<ComponentDesignMetadataBuilder> configure)
    {
        var builder = new ComponentDesignMetadataBuilder(type)
            .WithDisplay(
                displayName: displayName,
                category: "Sources",
                summary: summary,
                iconKey: iconKey,
                preferredNodeName: preferredNodeName,
                suggestedEditorWidth: suggestedEditorWidth)
            .AddResource(
                SourcesCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 0,
                summary: "Optional keyed clock for deterministic source timing and diagnostics.",
                valueType: nameof(TimeProvider));

        configure(builder);

        return builder.Build();
    }

    private static void AddNameOption(
        ComponentDesignMetadataBuilder builder,
        string defaultValue)
        => builder.AddOption(
            "name",
            OptionValueKind.Text,
            displayName: "Name",
            helperText: "Name emitted in source diagnostics and payloads.",
            defaultValue: defaultValue);

    private static void AddMaxItemsOption(ComponentDesignMetadataBuilder builder)
        => builder.AddOption(
            "maxItems",
            OptionValueKind.Number,
            displayName: "Max Items",
            helperText: "Optional maximum number of generated items to emit.",
            min: 1);

    private static void AddMillisecondsOption(
        ComponentDesignMetadataBuilder builder,
        string name,
        string displayName,
        string helperText)
        => builder.AddOption(
            name,
            OptionValueKind.Number,
            displayName: displayName,
            helperText: helperText,
            defaultValue: 0,
            min: 0);

    private static void AddBoundedCapacityOption(ComponentDesignMetadataBuilder builder)
        => builder.AddOption(
            "boundedCapacity",
            OptionValueKind.Number,
            displayName: "Bounded Capacity",
            helperText: "Maximum queued source messages.",
            defaultValue: 128,
            min: 1);

    private static void AddOutputPort(
        ComponentDesignMetadataBuilder builder,
        string valueType,
        string summary)
        => builder.AddOutputPort(
            SourcesCompositionPortNames.Output,
            displayName: "Output",
            group: "Messages",
            order: 0,
            summary: summary,
            valueType: valueType,
            isPrimary: true);
}
