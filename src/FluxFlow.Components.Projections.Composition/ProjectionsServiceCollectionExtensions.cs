using FluxFlow.Components.Designer;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Projections.Nodes;
using FluxFlow.Components.Projections.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Projections.Composition;

public static class ProjectionsServiceCollectionExtensions
{
    internal static ComponentDescriptor EventProjectionDescriptor { get; } = new(
        ProjectionsComponentTypes.EventProjection,
        CreateEventProjectionNode,
        inputs:
        [
            ComponentPorts.Metadata<ProjectionEvent>(
                ProjectionsComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<EventProjectionSnapshot>(
                ProjectionsComponentPortNames.Output)
        ]);

    public static IServiceCollection AddProjectionsComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(EventProjectionDescriptor);
        services.AddComponentDesignMetadataProvider<ProjectionsComponentDesignMetadataProvider>();
        return services;
    }

    private static ValueTask<ComponentInstance> CreateEventProjectionNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<EventProjectionOptions>();
        var clock = context.GetResource<TimeProvider>(
            ProjectionsComponentResourceNames.Clock);
        var node = new EventProjectionNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<ProjectionEvent>(
                    ProjectionsComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<EventProjectionSnapshot>(
                    ProjectionsComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
