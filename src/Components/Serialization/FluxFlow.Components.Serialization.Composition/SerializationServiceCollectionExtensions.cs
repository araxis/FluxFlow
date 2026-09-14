using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using FluxFlow.Components.Serialization.Nodes;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Serialization.Composition;

public static class SerializationServiceCollectionExtensions
{
    public static IServiceCollection AddSerialization(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddComponent(SerializationComponentDefinition.Types.JsonParse, SerializationServiceCollectionExtensions.CreateJsonParseNode)
            .AddComponent(SerializationComponentDefinition.Types.JsonStringify, SerializationServiceCollectionExtensions.CreateJsonStringifyNode)
            .AddComponent(SerializationComponentDefinition.Types.TextEncode, SerializationServiceCollectionExtensions.CreateTextEncodeNode)
            .AddComponent(SerializationComponentDefinition.Types.TextDecode, SerializationServiceCollectionExtensions.CreateTextDecodeNode)
            .AddComponent(SerializationComponentDefinition.Types.Base64Encode, SerializationServiceCollectionExtensions.CreateBase64EncodeNode)
            .AddComponent(SerializationComponentDefinition.Types.Base64Decode, SerializationServiceCollectionExtensions.CreateBase64DecodeNode);
    }

    internal static ValueTask<IFlowNode> CreateJsonParseNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new JsonParseNode(options, GetClock(context));
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    internal static ValueTask<IFlowNode> CreateJsonStringifyNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new JsonStringifyNode(options, GetClock(context));
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    internal static ValueTask<IFlowNode> CreateTextEncodeNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new TextEncodeNode(options, GetClock(context));
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    internal static ValueTask<IFlowNode> CreateTextDecodeNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new TextDecodeNode(options, GetClock(context));
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    internal static ValueTask<IFlowNode> CreateBase64EncodeNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new Base64EncodeNode(options, GetClock(context));
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    internal static ValueTask<IFlowNode> CreateBase64DecodeNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new Base64DecodeNode(options, GetClock(context));
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    private static TimeProvider? GetClock(ComponentActivationContext context)
        => context.GetResource<TimeProvider>(SerializationComponentDefinition.Resources.Clock);
}
