using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Projections.Nodes;
using FluxFlow.Components.Projections.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Projections.Composition;

public static class ProjectionsServiceCollectionExtensions
{
    public static IServiceCollection AddProjections(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(ProjectionsComponentDefinition.Types.EventProjection, ProjectionsServiceCollectionExtensions.CreateEventProjectionNode);
    }

    internal static ValueTask<IFlowNode> CreateEventProjectionNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<EventProjectionOptions>();
        var clock = context.GetResource<TimeProvider>(
            ProjectionsComponentDefinition.Resources.Clock);
        var node = new EventProjectionNode(options, clock);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }
}
