using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Sources.Composition;
using FluxFlow.Composition;

namespace FluxFlow.Components.Sources.Designer;

public static class SourcesDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddSourcesDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignMetadata(SourcesComponentDefinition.Types.Generated, DescribeGenerated)
            .AddDesignMetadata(SourcesComponentDefinition.Types.Sequence, DescribeSequence);
    }

    private static ComponentDesignMetadata DescribeGenerated(ComponentDesignMetadata metadata)
    {
        metadata = metadata.WithDisplay("Generated Source", "Sources", "Emits inline configured JSON items as independently owned JSON values.", "list-plus", "generated", 440);
        metadata = metadata.WithResource(SourcesComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic source timing and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
        metadata = metadata.WithOption(SourcesComponentDefinition.Options.Items, OptionValueKind.Json, "Items", "One inline JSON value or an array of JSON values to emit.", section: "Items", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Json);
        metadata = metadata.WithOption(SourcesComponentDefinition.Options.Loop, OptionValueKind.Boolean, "Loop", "Repeat configured items until maxItems is reached.", defaultValue: false, section: "Emission", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(SourcesComponentDefinition.Options.MaxItems, OptionValueKind.Number, "Max Items", "Optional maximum number of generated items to emit.", min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(SourcesComponentDefinition.Options.InitialDelayMilliseconds, OptionValueKind.Number, "Initial Delay Milliseconds", "Delay before the first item is emitted.", defaultValue: 0, min: 0, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(SourcesComponentDefinition.Options.IntervalMilliseconds, OptionValueKind.Number, "Interval Milliseconds", "Delay between emitted items.", defaultValue: 0, min: 0, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(SourcesComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded source production and reliable normal-data output.", defaultValue: 128, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata
            .WithPort(SourcesComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Messages", 0, "Generated JSON value.", true)
            .WithPort(SourcesComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 1, "Best-effort generated-source diagnostics.");
        return metadata;
    }

    private static ComponentDesignMetadata DescribeSequence(ComponentDesignMetadata metadata)
    {
        metadata = metadata.WithDisplay("Sequence Source", "Sources", "Emits typed numeric sequence items.", "list-ordered", "sequence", 420);
        metadata = metadata.WithResource(SourcesComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic source timing and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
        metadata = metadata.WithOption(SourcesComponentDefinition.Options.Start, OptionValueKind.Number, "Start", "First numeric value emitted.", defaultValue: 1, section: "Sequence", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(SourcesComponentDefinition.Options.Step, OptionValueKind.Number, "Step", "Amount added for each item; cannot be zero.", defaultValue: 1, section: "Sequence", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(SourcesComponentDefinition.Options.Count, OptionValueKind.Number, "Count", "Number of sequence items to emit.", defaultValue: 1, min: 1, section: "Sequence", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(SourcesComponentDefinition.Options.InitialDelayMilliseconds, OptionValueKind.Number, "Initial Delay Milliseconds", "Delay before the first item is emitted.", defaultValue: 0, min: 0, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(SourcesComponentDefinition.Options.IntervalMilliseconds, OptionValueKind.Number, "Interval Milliseconds", "Delay between emitted items.", defaultValue: 0, min: 0, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(SourcesComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded source production and reliable normal-data output.", defaultValue: 128, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata
            .WithPort(SourcesComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Messages", 0, "Numeric sequence item.", true)
            .WithPort(SourcesComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 1, "Best-effort sequence-source diagnostics.");
        return metadata;
    }
}
