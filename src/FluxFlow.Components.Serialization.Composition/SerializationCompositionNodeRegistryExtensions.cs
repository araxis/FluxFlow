using FluxFlow.Components.Serialization.Contracts;
using FluxFlow.Components.Serialization.Nodes;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;

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
                CompositionPorts.Metadata<JsonParseRequest>(
                    SerializationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<JsonParseResult>(
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
                CompositionPorts.Metadata<JsonStringifyRequest>(
                    SerializationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<JsonStringifyResult>(
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
                CompositionPorts.Metadata<TextEncodeRequest>(
                    SerializationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<TextEncodeResult>(
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
                CompositionPorts.Metadata<TextDecodeRequest>(
                    SerializationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<TextDecodeResult>(
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
                CompositionPorts.Metadata<Base64EncodeRequest>(
                    SerializationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<Base64EncodeResult>(
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
                CompositionPorts.Metadata<Base64DecodeRequest>(
                    SerializationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<Base64DecodeResult>(
                    SerializationCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateJsonParseNode(
        CompositionNodeFactoryContext context)
        => CreateSerializationNode<JsonParseRequest, JsonParseResult>(
            context,
            (options, clock) => new JsonParseNode(options, clock));

    private static ValueTask<ComposedNode> CreateJsonStringifyNode(
        CompositionNodeFactoryContext context)
        => CreateSerializationNode<JsonStringifyRequest, JsonStringifyResult>(
            context,
            (options, clock) => new JsonStringifyNode(options, clock));

    private static ValueTask<ComposedNode> CreateTextEncodeNode(
        CompositionNodeFactoryContext context)
        => CreateSerializationNode<TextEncodeRequest, TextEncodeResult>(
            context,
            (options, clock) => new TextEncodeNode(options, clock));

    private static ValueTask<ComposedNode> CreateTextDecodeNode(
        CompositionNodeFactoryContext context)
        => CreateSerializationNode<TextDecodeRequest, TextDecodeResult>(
            context,
            (options, clock) => new TextDecodeNode(options, clock));

    private static ValueTask<ComposedNode> CreateBase64EncodeNode(
        CompositionNodeFactoryContext context)
        => CreateSerializationNode<Base64EncodeRequest, Base64EncodeResult>(
            context,
            (options, clock) => new Base64EncodeNode(options, clock));

    private static ValueTask<ComposedNode> CreateBase64DecodeNode(
        CompositionNodeFactoryContext context)
        => CreateSerializationNode<Base64DecodeRequest, Base64DecodeResult>(
            context,
            (options, clock) => new Base64DecodeNode(options, clock));

    private static ValueTask<ComposedNode> CreateSerializationNode<TInput, TOutput>(
        CompositionNodeFactoryContext context,
        Func<SerializationNodeOptions, TimeProvider?, SerializationTransformNode<TInput, TOutput>> factory)
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
                CompositionPorts.Output<TOutput>(
                    SerializationCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }
}
