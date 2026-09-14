using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Sources.Composition;

public static class SourcesServiceCollectionExtensions
{
    private const string GeneratedItemsConfigurationName = "items";

    public static IServiceCollection AddSources(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddComponent(SourcesComponentDefinition.Types.Generated, SourcesServiceCollectionExtensions.CreateGeneratedSourceNode)
            .AddComponent(SourcesComponentDefinition.Types.Sequence, SourcesServiceCollectionExtensions.CreateSequenceSourceNode);
    }

    internal static ValueTask<IFlowNode> CreateGeneratedSourceNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<GeneratedSourceOptions>();
        var items = context.GetConfigurationValue<JsonElement>(GeneratedItemsConfigurationName);
        var clock = context.GetResource<TimeProvider>(
            SourcesComponentDefinition.Resources.Clock);
        var node = GeneratedSourceNode.Create(options, items, clock);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    internal static ValueTask<IFlowNode> CreateSequenceSourceNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SequenceSourceOptions>();
        var clock = context.GetResource<TimeProvider>(
            SourcesComponentDefinition.Resources.Clock);
        var node = new SequenceSourceNode(options, clock);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }
}
