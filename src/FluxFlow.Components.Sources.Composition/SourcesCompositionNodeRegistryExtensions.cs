using System.Collections.Immutable;
using System.Text.Json;
using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Sources.Composition;

public static class SourcesCompositionNodeRegistryExtensions
{
    private const string GeneratedItemsConfigurationName = "items";

    public static CompositionNodeRegistry RegisterGeneratedSource(
        this CompositionNodeRegistry registry,
        string nodeType = SourcesCompositionNodeTypes.Generated)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            SourcesCompositionNodeTypes.GeneratedDescriptor,
            CreateGeneratedSourceNode,
            outputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    SourcesCompositionPortNames.Output)
            ],
            registrationType: nodeType);
    }

    public static CompositionNodeRegistry RegisterSequenceSource(
        this CompositionNodeRegistry registry,
        string nodeType = SourcesCompositionNodeTypes.Sequence)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateSequenceSourceNode,
            outputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    SourcesCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateGeneratedSourceNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<GeneratedSourceOptions>();
        var items = DecodeGeneratedItems(context);
        var clock = context.GetResource<TimeProvider>(
            SourcesCompositionResourceNames.Clock);
        var node = new GeneratedSourceNode(options, items, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            outputs:
            [
                CompositionPorts.Output<FlowValue>(
                    SourcesCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComposedNode> CreateSequenceSourceNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SequenceSourceOptions>();
        var clock = context.GetResource<TimeProvider>(
            SourcesCompositionResourceNames.Clock);
        var node = new SequenceSourceNode(options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            outputs:
            [
                CompositionPorts.Output<FlowValue>(
                    SourcesCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static IReadOnlyList<FlowValue> DecodeGeneratedItems(
        CompositionNodeFactoryContext context)
    {
        var configuredItems = context.GetConfigurationValue<JsonElement>(
            GeneratedItemsConfigurationName);
        if (configuredItems.ValueKind == JsonValueKind.Undefined)
        {
            return [];
        }

        return configuredItems.ValueKind == JsonValueKind.Array
            ? configuredItems.EnumerateArray().Select(DecodeFlowValue).ToArray()
            : [DecodeFlowValue(configuredItems)];
    }

    private static FlowValue DecodeFlowValue(JsonElement element)
    {
        var bytes = ImmutableArray.CreateRange(
            JsonSerializer.SerializeToUtf8Bytes(element));
        return new JsonFlowContentCodec().Decode(bytes, encoding: null);
    }
}
