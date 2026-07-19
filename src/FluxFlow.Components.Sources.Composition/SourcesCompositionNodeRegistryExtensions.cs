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
            nodeType,
            CreateFlowValueGeneratedSourceNode,
            outputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    SourcesCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterGeneratedSource<TOutput>(
        this CompositionNodeRegistry registry,
        string nodeType = SourcesCompositionNodeTypes.Generated)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateGeneratedSourceNode<TOutput>,
            outputs:
            [
                CompositionPorts.Metadata<TOutput>(
                    SourcesCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterSequenceSource(
        this CompositionNodeRegistry registry,
        string nodeType = SourcesCompositionNodeTypes.Sequence)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateFlowValueSequenceSourceNode,
            outputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    SourcesCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateFlowValueGeneratedSourceNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<FlowValueGeneratedSourceOptions>();
        var items = DecodeGeneratedItems(context);
        var clock = context.GetResource<TimeProvider>(
            SourcesCompositionResourceNames.Clock);
        var node = new FlowValueGeneratedSourceNode(options, items, clock);

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

    private static ValueTask<ComposedNode> CreateGeneratedSourceNode<TOutput>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<GeneratedSourceOptions>();
        var items = context.GetConfigurationValue<TOutput[]>(
            GeneratedItemsConfigurationName) ?? [];
        var clock = context.GetResource<TimeProvider>(
            SourcesCompositionResourceNames.Clock);
        var node = new GeneratedSourceNode<TOutput>(options, items, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            outputs:
            [
                CompositionPorts.Output<TOutput>(
                    SourcesCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateFlowValueSequenceSourceNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SequenceSourceOptions>();
        var clock = context.GetResource<TimeProvider>(
            SourcesCompositionResourceNames.Clock);
        var node = new FlowValueSequenceSourceNode(options, clock);

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
        if (!context.Configuration.TryGetValue(
            GeneratedItemsConfigurationName,
            out var configuredItems))
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
