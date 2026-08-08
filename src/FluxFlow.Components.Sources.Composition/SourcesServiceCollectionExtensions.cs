using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Sources.Composition;

public static class SourcesServiceCollectionExtensions
{
    private const string GeneratedItemsConfigurationName = "items";

    public static FluxFlowRegistrationBuilder AddSources(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignedComponent(SourcesComponents.GeneratedSource)
            .AddDesignedComponent(SourcesComponents.SequenceSource);
    }

    internal static void ConfigureGenerated(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, "Generated Source", "Emits inline configured JSON items as independently owned JSON values.", "list-plus", "generated", 440);
        AddName(component, GeneratedSourceOptions.DefaultName);
        component.AddOption<JsonElement>(SourcesComponentDefinition.Options.Items, OptionValueKind.Json, "Items", "One inline JSON value or an array of JSON values to emit.", section: "Items", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Json);
        component.AddOption<bool>(SourcesComponentDefinition.Options.Loop, OptionValueKind.Boolean, "Loop", "Repeat configured items until maxItems is reached.", defaultValue: false, section: "Emission", importance: OptionDesignMetadataAttributeValues.Advanced);
        component.AddOption<int?>(SourcesComponentDefinition.Options.MaxItems, OptionValueKind.Number, "Max Items", "Optional maximum number of generated items to emit.", min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        AddTiming(component, SourcesComponentDefinition.Options.InitialDelayMilliseconds, "Initial Delay Milliseconds", "Delay before the first item is emitted.");
        AddTiming(component, SourcesComponentDefinition.Options.IntervalMilliseconds, "Interval Milliseconds", "Delay between emitted items.");
        AddCapacity(component);
        component
            .UseFactory(CreateGeneratedSourceNode)
            .HasOutput(SourcesComponentDefinition.Ports.Output, static node => node.Output, "Output", "Messages", 0, "Generated JSON value.", true)
            .HasEvents(SourcesComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 1, "Best-effort generated-source diagnostics.");
    }

    internal static void ConfigureSequence(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, "Sequence Source", "Emits typed numeric sequence items.", "list-ordered", "sequence", 420);
        AddName(component, SequenceSourceOptions.DefaultName);
        component.AddOption<long>(SourcesComponentDefinition.Options.Start, OptionValueKind.Number, "Start", "First numeric value emitted.", defaultValue: 1, section: "Sequence", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<long>(SourcesComponentDefinition.Options.Step, OptionValueKind.Number, "Step", "Amount added for each item; cannot be zero.", defaultValue: 1, section: "Sequence", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<int>(SourcesComponentDefinition.Options.Count, OptionValueKind.Number, "Count", "Number of sequence items to emit.", defaultValue: 1, min: 1, section: "Sequence", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Number);
        AddTiming(component, SourcesComponentDefinition.Options.InitialDelayMilliseconds, "Initial Delay Milliseconds", "Delay before the first item is emitted.");
        AddTiming(component, SourcesComponentDefinition.Options.IntervalMilliseconds, "Interval Milliseconds", "Delay between emitted items.");
        AddCapacity(component);
        component
            .UseFactory(CreateSequenceSourceNode)
            .HasOutput(SourcesComponentDefinition.Ports.Output, static node => node.Output, "Output", "Messages", 0, "Numeric sequence item.", true)
            .HasEvents(SourcesComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 1, "Best-effort sequence-source diagnostics.");
    }

    private static void ConfigureCommon(ComponentRegistrationBuilder component, string displayName, string summary, string iconKey, string preferredNodeName, int width)
    {
        component.WithDisplay(displayName, "Sources", summary, iconKey, preferredNodeName, width);
        component.AddResource<TimeProvider>(SourcesComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic source timing and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
    }

    private static void AddName(ComponentRegistrationBuilder component, string defaultValue)
        => component.AddOption<string>(SourcesComponentDefinition.Options.Name, OptionValueKind.Text, "Name", "Name emitted in source diagnostics and payloads.", defaultValue: defaultValue, section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);

    private static void AddTiming(ComponentRegistrationBuilder component, string name, string displayName, string helperText)
        => component.AddOption<int>(name, OptionValueKind.Number, displayName, helperText, defaultValue: 0, min: 0, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);

    private static void AddCapacity(ComponentRegistrationBuilder component)
        => component.AddOption<int>(SourcesComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded source production and reliable normal-data output.", defaultValue: 128, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);

    private static GeneratedSourceNode CreateGeneratedSourceNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<GeneratedSourceOptions>();
        var items = DecodeGeneratedItems(context);
        var clock = context.GetResource<TimeProvider>(
            SourcesComponentDefinition.Resources.Clock);
        return new GeneratedSourceNode(options, items, clock);
    }

    private static SequenceSourceNode CreateSequenceSourceNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SequenceSourceOptions>();
        var clock = context.GetResource<TimeProvider>(
            SourcesComponentDefinition.Resources.Clock);
        return new SequenceSourceNode(options, clock);
    }

    private static IReadOnlyList<JsonElement> DecodeGeneratedItems(
        ComponentActivationContext context)
    {
        var configuredItems = context.GetConfigurationValue<JsonElement>(
            GeneratedItemsConfigurationName);
        if (configuredItems.ValueKind == JsonValueKind.Undefined)
        {
            return [];
        }

        return configuredItems.ValueKind == JsonValueKind.Array
            ? configuredItems.EnumerateArray().Select(static item => item.Clone()).ToArray()
            : [configuredItems.Clone()];
    }
}
