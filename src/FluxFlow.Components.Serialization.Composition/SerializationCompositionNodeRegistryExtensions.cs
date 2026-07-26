using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Serialization.Nodes;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Serialization.Composition;

public static class SerializationCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterJsonParse(
        this CompositionNodeRegistry registry,
        string nodeType = SerializationCompositionNodeTypes.JsonParse)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        return registry.Register(
            nodeType,
            CreateJsonParseNode,
            inputs:
            [
                CompositionPorts.Metadata<FlowContent>(
                    SerializationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<JsonElement>(
                    SerializationCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterJsonStringify(
        this CompositionNodeRegistry registry,
        string nodeType = SerializationCompositionNodeTypes.JsonStringify)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        return registry.Register(
            nodeType,
            CreateJsonStringifyNode,
            inputs:
            [
                CompositionPorts.Metadata<JsonElement>(
                    SerializationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowContent>(
                    SerializationCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterTextEncode(
        this CompositionNodeRegistry registry,
        string nodeType = SerializationCompositionNodeTypes.TextEncode)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        return registry.Register(
            nodeType,
            CreateTextEncodeNode,
            inputs:
            [
                CompositionPorts.Metadata<string>(
                    SerializationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowContent>(
                    SerializationCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterTextDecode(
        this CompositionNodeRegistry registry,
        string nodeType = SerializationCompositionNodeTypes.TextDecode)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        return registry.Register(
            nodeType,
            CreateTextDecodeNode,
            inputs:
            [
                CompositionPorts.Metadata<FlowContent>(
                    SerializationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<string>(
                    SerializationCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterBase64Encode(
        this CompositionNodeRegistry registry,
        string nodeType = SerializationCompositionNodeTypes.Base64Encode)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        return registry.Register(
            nodeType,
            CreateBase64EncodeNode,
            inputs:
            [
                CompositionPorts.Metadata<FlowContent>(
                    SerializationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<string>(
                    SerializationCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterBase64Decode(
        this CompositionNodeRegistry registry,
        string nodeType = SerializationCompositionNodeTypes.Base64Decode)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        return registry.Register(
            nodeType,
            CreateBase64DecodeNode,
            inputs:
            [
                CompositionPorts.Metadata<string>(
                    SerializationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowContent>(
                    SerializationCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateJsonParseNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new JsonParseNode(options, GetClock(context));
        return Compose<FlowContent, JsonElement>(node, node.Input, node.Output, node.Events);
    }

    private static ValueTask<ComposedNode> CreateJsonStringifyNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new JsonStringifyNode(options, GetClock(context));
        return Compose<JsonElement, FlowContent>(node, node.Input, node.Output, node.Events);
    }

    private static ValueTask<ComposedNode> CreateTextEncodeNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new TextEncodeNode(options, GetClock(context));
        return Compose<string, FlowContent>(node, node.Input, node.Output, node.Events);
    }

    private static ValueTask<ComposedNode> CreateTextDecodeNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new TextDecodeNode(options, GetClock(context));
        return Compose<FlowContent, string>(node, node.Input, node.Output, node.Events);
    }

    private static ValueTask<ComposedNode> CreateBase64EncodeNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new Base64EncodeNode(options, GetClock(context));
        return Compose<FlowContent, string>(node, node.Input, node.Output, node.Events);
    }

    private static ValueTask<ComposedNode> CreateBase64DecodeNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new Base64DecodeNode(options, GetClock(context));
        return Compose<string, FlowContent>(node, node.Input, node.Output, node.Events);
    }

    private static TimeProvider? GetClock(CompositionNodeFactoryContext context)
        => context.GetResource<TimeProvider>(SerializationCompositionResourceNames.Clock);

    private static ValueTask<ComposedNode> Compose<TInput, TOutput>(
        IFlowNode node,
        ITargetBlock<FlowMessage<TInput>> input,
        ISourceBlock<FlowMessage<TOutput>> output,
        ISourceBlock<FlowEvent> events)
    {
        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    SerializationCompositionPortNames.Input,
                    input)
            ],
            outputs:
            [
                CompositionPorts.Output<TOutput>(
                    SerializationCompositionPortNames.Output,
                    output)
            ],
            events: events));
    }
}
