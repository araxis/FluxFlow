using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Serialization.Nodes;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Serialization.Composition;

public static class SerializationServiceCollectionExtensions
{
    internal static ComponentDescriptor JsonParseDescriptor { get; } = CreateDescriptor<FlowContent, JsonElement>(
        SerializationComponentTypes.JsonParse,
        CreateJsonParseNode);
    internal static ComponentDescriptor JsonStringifyDescriptor { get; } = CreateDescriptor<JsonElement, FlowContent>(
        SerializationComponentTypes.JsonStringify,
        CreateJsonStringifyNode);
    internal static ComponentDescriptor TextEncodeDescriptor { get; } = CreateDescriptor<string, FlowContent>(
        SerializationComponentTypes.TextEncode,
        CreateTextEncodeNode);
    internal static ComponentDescriptor TextDecodeDescriptor { get; } = CreateDescriptor<FlowContent, string>(
        SerializationComponentTypes.TextDecode,
        CreateTextDecodeNode);
    internal static ComponentDescriptor Base64EncodeDescriptor { get; } = CreateDescriptor<FlowContent, string>(
        SerializationComponentTypes.Base64Encode,
        CreateBase64EncodeNode);
    internal static ComponentDescriptor Base64DecodeDescriptor { get; } = CreateDescriptor<string, FlowContent>(
        SerializationComponentTypes.Base64Decode,
        CreateBase64DecodeNode);

    public static IServiceCollection AddSerializationComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(JsonParseDescriptor);
        services.AddFluxFlowComponent(JsonStringifyDescriptor);
        services.AddFluxFlowComponent(TextEncodeDescriptor);
        services.AddFluxFlowComponent(TextDecodeDescriptor);
        services.AddFluxFlowComponent(Base64EncodeDescriptor);
        services.AddFluxFlowComponent(Base64DecodeDescriptor);
        services.AddComponentDesignMetadataProvider<SerializationComponentDesignMetadataProvider>();
        return services;
    }

    private static ComponentDescriptor CreateDescriptor<TInput, TOutput>(
        string type,
        ComponentFactory factory)
        => new(
            type,
            factory,
            inputs:
            [
                ComponentPorts.Metadata<TInput>(SerializationComponentPortNames.Input)
            ],
            outputs:
            [
                ComponentPorts.Metadata<TOutput>(SerializationComponentPortNames.Output)
            ]);

    private static ValueTask<ComponentInstance> CreateJsonParseNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new JsonParseNode(options, GetClock(context));
        return Compose<FlowContent, JsonElement>(node, node.Input, node.Output, node.Events);
    }

    private static ValueTask<ComponentInstance> CreateJsonStringifyNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new JsonStringifyNode(options, GetClock(context));
        return Compose<JsonElement, FlowContent>(node, node.Input, node.Output, node.Events);
    }

    private static ValueTask<ComponentInstance> CreateTextEncodeNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new TextEncodeNode(options, GetClock(context));
        return Compose<string, FlowContent>(node, node.Input, node.Output, node.Events);
    }

    private static ValueTask<ComponentInstance> CreateTextDecodeNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new TextDecodeNode(options, GetClock(context));
        return Compose<FlowContent, string>(node, node.Input, node.Output, node.Events);
    }

    private static ValueTask<ComponentInstance> CreateBase64EncodeNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new Base64EncodeNode(options, GetClock(context));
        return Compose<FlowContent, string>(node, node.Input, node.Output, node.Events);
    }

    private static ValueTask<ComponentInstance> CreateBase64DecodeNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new Base64DecodeNode(options, GetClock(context));
        return Compose<string, FlowContent>(node, node.Input, node.Output, node.Events);
    }

    private static TimeProvider? GetClock(ComponentActivationContext context)
        => context.GetResource<TimeProvider>(SerializationComponentResourceNames.Clock);

    private static ValueTask<ComponentInstance> Compose<TInput, TOutput>(
        IFlowNode node,
        ITargetBlock<FlowMessage<TInput>> input,
        ISourceBlock<FlowMessage<TOutput>> output,
        ISourceBlock<FlowEvent> events)
    {
        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<TInput>(
                    SerializationComponentPortNames.Input,
                    input)
            ],
            outputs:
            [
                ComponentPorts.Output<TOutput>(
                    SerializationComponentPortNames.Output,
                    output)
            ],
            events: events));
    }
}
