using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Options;

namespace FluxFlow.Components.Sources.Composition;

public static partial class SourcesComponentDefinition
{
    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
        =>
        [
            CreateGeneratedMetadata(),
            CreateSequenceMetadata()
        ];

    private static ComponentDesignMetadata CreateGeneratedMetadata()
        => CreateSourceMetadata(
            SourcesComponentDefinition.Types.Generated,
            "Generated Source",
            "Emits inline configured JSON items as independently owned JSON values.",
            "list-plus",
            "generated",
            suggestedEditorWidth: 440,
            builder =>
            {
                AddNameOption(builder, GeneratedSourceOptions.DefaultName);
                builder
                    .AddOption(
                        Options.Items,
                        OptionValueKind.Json,
                        displayName: "Items",
                        helperText: "One inline JSON value or an array of JSON values to emit.",
                        attributes: OptionAttributes(
                            "Items",
                            OptionDesignMetadataAttributeValues.Primary,
                            OptionDesignMetadataAttributeValues.Json))
                    .AddOption(
                        Options.Loop,
                        OptionValueKind.Boolean,
                        displayName: "Loop",
                        helperText: "Repeat configured items until maxItems is reached.",
                        defaultValue: false,
                        attributes: OptionAttributes(
                            "Emission",
                            OptionDesignMetadataAttributeValues.Advanced));
                AddMaxItemsOption(builder);
                AddMillisecondsOption(
                    builder,
                    Options.InitialDelayMilliseconds,
                    "Initial Delay Milliseconds",
                    "Delay before the first item is emitted.");
                AddMillisecondsOption(
                    builder,
                    Options.IntervalMilliseconds,
                    "Interval Milliseconds",
                    "Delay between emitted items.");
                AddBoundedCapacityOption(builder);
                AddOutputPort(builder, nameof(JsonElement), "Generated JSON value.");
            });

    private static ComponentDesignMetadata CreateSequenceMetadata()
        => CreateSourceMetadata(
            SourcesComponentDefinition.Types.Sequence,
            "Sequence Source",
            "Emits typed numeric sequence items.",
            "list-ordered",
            "sequence",
            suggestedEditorWidth: 420,
            builder =>
            {
                AddNameOption(builder, SequenceSourceOptions.DefaultName);
                builder
                    .AddOption(
                        Options.Start,
                        OptionValueKind.Number,
                        displayName: "Start",
                        helperText: "First numeric value emitted.",
                        defaultValue: 1,
                        attributes: OptionAttributes(
                            "Sequence",
                            OptionDesignMetadataAttributeValues.Advanced,
                            OptionDesignMetadataAttributeValues.Number))
                    .AddOption(
                        Options.Step,
                        OptionValueKind.Number,
                        displayName: "Step",
                        helperText: "Amount added for each item; cannot be zero.",
                        defaultValue: 1,
                        attributes: OptionAttributes(
                            "Sequence",
                            OptionDesignMetadataAttributeValues.Advanced,
                            OptionDesignMetadataAttributeValues.Number))
                    .AddOption(
                        Options.Count,
                        OptionValueKind.Number,
                        displayName: "Count",
                        helperText: "Number of sequence items to emit.",
                        defaultValue: 1,
                        min: 1,
                        attributes: OptionAttributes(
                            "Sequence",
                            OptionDesignMetadataAttributeValues.Primary,
                            OptionDesignMetadataAttributeValues.Number));
                AddMillisecondsOption(
                    builder,
                    Options.InitialDelayMilliseconds,
                    "Initial Delay Milliseconds",
                    "Delay before the first item is emitted.");
                AddMillisecondsOption(
                    builder,
                    Options.IntervalMilliseconds,
                    "Interval Milliseconds",
                    "Delay between emitted items.");
                AddBoundedCapacityOption(builder);
                AddOutputPort(builder, nameof(SequenceItem), "Numeric sequence item.");
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
                SourcesComponentDefinition.Resources.Clock,
                displayName: "Clock",
                order: 0,
                summary: "Optional keyed clock for deterministic source timing and diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "Resources.{name}"));

        configure(builder);

        return builder.Build();
    }

    private static void AddNameOption(
        ComponentDesignMetadataBuilder builder,
        string defaultValue)
        => builder.AddOption(
            Options.Name,
            OptionValueKind.Text,
            displayName: "Name",
            helperText: "Name emitted in source diagnostics and payloads.",
            defaultValue: defaultValue,
            attributes: OptionAttributes(
                "Diagnostics",
                OptionDesignMetadataAttributeValues.Advanced,
                OptionDesignMetadataAttributeValues.Text));

    private static void AddMaxItemsOption(ComponentDesignMetadataBuilder builder)
        => builder.AddOption(
            Options.MaxItems,
            OptionValueKind.Number,
            displayName: "Max Items",
            helperText: "Optional maximum number of generated items to emit.",
            min: 1,
            attributes: OptionAttributes(
                "Runtime",
                OptionDesignMetadataAttributeValues.Advanced,
                OptionDesignMetadataAttributeValues.Number));

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
            min: 0,
            attributes: OptionAttributes(
                "Timing",
                OptionDesignMetadataAttributeValues.Advanced,
                OptionDesignMetadataAttributeValues.Number));

    private static void AddBoundedCapacityOption(ComponentDesignMetadataBuilder builder)
        => builder.AddOption(OptionDesignMetadataFactory.BoundedCapacity(
            128,
            "Maximum queued source messages."));

    private static IReadOnlyDictionary<string, string> OptionAttributes(
        string section,
        string importance,
        string? editor = null)
        => OptionDesignMetadataAttributes.Create(
            section: section,
            importance: importance,
            editor: editor);

    private static void AddOutputPort(
        ComponentDesignMetadataBuilder builder,
        string valueType,
        string summary)
        => builder.AddOutputPort(
            SourcesComponentDefinition.Ports.Output,
            displayName: Ports.Output,
            group: "Messages",
            order: 0,
            summary: summary,
            valueType: valueType,
            isPrimary: true);


    public static class Options
    {
        public const string Name = "name";
        public const string Items = "items";
        public const string Loop = "loop";
        public const string MaxItems = "maxItems";
        public const string InitialDelayMilliseconds = "initialDelayMilliseconds";
        public const string IntervalMilliseconds = "intervalMilliseconds";
        public const string BoundedCapacity = "boundedCapacity";
        public const string Start = "start";
        public const string Step = "step";
        public const string Count = "count";
    }

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.Generated =>
            [
                ComponentOptions.Metadata<string>(Options.Name),
                ComponentOptions.Metadata<JsonElement>(Options.Items),
                ComponentOptions.Metadata<bool>(Options.Loop),
                ComponentOptions.Metadata<int?>(Options.MaxItems),
                ComponentOptions.Metadata<int>(Options.InitialDelayMilliseconds),
                ComponentOptions.Metadata<int>(Options.IntervalMilliseconds),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            Types.Sequence =>
            [
                ComponentOptions.Metadata<string>(Options.Name),
                ComponentOptions.Metadata<long>(Options.Start),
                ComponentOptions.Metadata<long>(Options.Step),
                ComponentOptions.Metadata<int>(Options.Count),
                ComponentOptions.Metadata<int>(Options.InitialDelayMilliseconds),
                ComponentOptions.Metadata<int>(Options.IntervalMilliseconds),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.Generated =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Sequence =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string Generated = "source.items";
        public const string Sequence = "source.sequence";
    }

    public static class Ports
    {
        public const string Output = "Output";
    }

    public static class Resources
    {
        public const string Clock = "clock";
    }
}
