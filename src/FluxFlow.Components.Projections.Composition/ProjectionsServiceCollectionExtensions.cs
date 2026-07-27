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
    private static readonly Lazy<IReadOnlyCollection<ComponentDesignDeclaration>> DeclarationSet =
        new(CreateDeclarations);

    internal static IReadOnlyCollection<ComponentDesignDeclaration> Declarations =>
        DeclarationSet.Value;

    private static IReadOnlyCollection<ComponentDesignDeclaration> CreateDeclarations() =>
        ComponentDesignDeclaration.CreateRange(
            [
                EventProjectionDescriptor
            ],
            ProjectionsComponentDefinition.CreateMetadata());

    internal static ComponentDescriptor EventProjectionDescriptor { get; } = new(
        ProjectionsComponentDefinition.Types.EventProjection,
        CreateEventProjectionNode,
        inputs:
        [
            ComponentPorts.Metadata<ProjectionEvent>(
                ProjectionsComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<EventProjectionSnapshot>(
                ProjectionsComponentDefinition.Ports.Output)
        ],
        options: ProjectionsComponentDefinition.CreateOptions(ProjectionsComponentDefinition.Types.EventProjection),
        resources: ProjectionsComponentDefinition.CreateResources(ProjectionsComponentDefinition.Types.EventProjection));

    public static IServiceCollection AddProjectionsComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclarations(Declarations);
        return services;
    }

    private static ValueTask<ComponentInstance> CreateEventProjectionNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<EventProjectionOptions>();
        var clock = context.GetResource<TimeProvider>(
            ProjectionsComponentDefinition.Resources.Clock);
        var node = new EventProjectionNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<ProjectionEvent>(
                    ProjectionsComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<EventProjectionSnapshot>(
                    ProjectionsComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
