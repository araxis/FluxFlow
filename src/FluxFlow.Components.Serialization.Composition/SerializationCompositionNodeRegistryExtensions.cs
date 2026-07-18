using FluxFlow.Components.Serialization.Nodes;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

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
                CompositionPorts.Metadata<FlowResult<FlowValue>>(
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
                CompositionPorts.Metadata<FlowValue>(
                    SerializationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<FlowContent>>(
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
                CompositionPorts.Metadata<FlowValue>(
                    SerializationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<FlowContent>>(
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
                CompositionPorts.Metadata<FlowResult<FlowValue>>(
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
                CompositionPorts.Metadata<FlowResult<FlowValue>>(
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
                CompositionPorts.Metadata<FlowValue>(
                    SerializationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<FlowContent>>(
                    SerializationCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateJsonParseNode(
        CompositionNodeFactoryContext context)
        => CreateSerializationNode<FlowContent, FlowValue>(
            context,
            (options, clock) => new FlowContentJsonParseNode(options, clock));

    private static ValueTask<ComposedNode> CreateJsonStringifyNode(
        CompositionNodeFactoryContext context)
        => CreateSerializationNode<FlowValue, FlowContent>(
            context,
            (options, clock) => new FlowValueJsonStringifyNode(options, clock));

    private static ValueTask<ComposedNode> CreateTextEncodeNode(
        CompositionNodeFactoryContext context)
        => CreateSerializationNode<FlowValue, FlowContent>(
            context,
            (options, clock) => new FlowValueTextEncodeNode(options, clock));

    private static ValueTask<ComposedNode> CreateTextDecodeNode(
        CompositionNodeFactoryContext context)
        => CreateSerializationNode<FlowContent, FlowValue>(
            context,
            (options, clock) => new FlowContentTextDecodeNode(options, clock));

    private static ValueTask<ComposedNode> CreateBase64EncodeNode(
        CompositionNodeFactoryContext context)
        => CreateSerializationNode<FlowContent, FlowValue>(
            context,
            (options, clock) => new FlowContentBase64EncodeNode(options, clock));

    private static ValueTask<ComposedNode> CreateBase64DecodeNode(
        CompositionNodeFactoryContext context)
        => CreateSerializationNode<FlowValue, FlowContent>(
            context,
            (options, clock) => new FlowValueBase64DecodeNode(options, clock));

    private static ValueTask<ComposedNode> CreateSerializationNode<TInput, TOutput>(
        CompositionNodeFactoryContext context,
        Func<
            SerializationNodeOptions,
            TimeProvider?,
            FlowSerializationNode<TInput, TOutput>> factory)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var clock = context.GetResource<TimeProvider>(
            SerializationCompositionResourceNames.Clock);
        var node = factory(options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    SerializationCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowResult<TOutput>>(
                    SerializationCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
