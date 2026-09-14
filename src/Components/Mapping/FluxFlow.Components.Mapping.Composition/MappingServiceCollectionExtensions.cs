using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Nodes;
using FluxFlow.Data;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Composition;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Mapping.Composition;

public static class MappingServiceCollectionExtensions
{
    public static IServiceCollection AddMapping(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(MappingComponentDefinition.Types.Mapper, MappingServiceCollectionExtensions.CreateJsonMapperNode);
    }

    internal static ValueTask<IFlowNode> CreateJsonMapperNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<MapperOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            MappingComponentDefinition.Resources.Engine);
        var contextFactory = context.GetResource<IMappingContextFactory>(
            MappingComponentDefinition.Resources.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            MappingComponentDefinition.Resources.Clock);
        var node = new JsonMapperNode(
            options,
            expressionEngine,
            contextFactory,
            clock);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }
}
