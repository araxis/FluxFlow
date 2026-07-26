using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Sources.Composition;

public static class SourcesServiceCollectionExtensions
{
    private const string GeneratedItemsConfigurationName = "items";

    internal static ComponentDescriptor GeneratedDescriptor { get; } = new(
        SourcesComponentTypes.Generated,
        CreateGeneratedSourceNode,
        outputs:
        [
            ComponentPorts.Metadata<JsonElement>(SourcesComponentPortNames.Output)
        ],
        aliases: [SourcesComponentTypes.LegacyGenerated]);

    internal static ComponentDescriptor SequenceDescriptor { get; } = new(
        SourcesComponentTypes.Sequence,
        CreateSequenceSourceNode,
        outputs:
        [
            ComponentPorts.Metadata<SequenceItem>(SourcesComponentPortNames.Output)
        ]);

    public static IServiceCollection AddSourcesComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(GeneratedDescriptor);
        services.AddFluxFlowComponent(SequenceDescriptor);
        services.AddComponentDesignMetadataProvider<SourcesComponentDesignMetadataProvider>();
        return services;
    }

    private static ValueTask<ComponentInstance> CreateGeneratedSourceNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<GeneratedSourceOptions>();
        var items = DecodeGeneratedItems(context);
        var clock = context.GetResource<TimeProvider>(
            SourcesComponentResourceNames.Clock);
        var node = new GeneratedSourceNode(options, items, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            outputs:
            [
                ComponentPorts.Output<JsonElement>(
                    SourcesComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateSequenceSourceNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SequenceSourceOptions>();
        var clock = context.GetResource<TimeProvider>(
            SourcesComponentResourceNames.Clock);
        var node = new SequenceSourceNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            outputs:
            [
                ComponentPorts.Output<SequenceItem>(
                    SourcesComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
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
