using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Projections.Nodes;
using FluxFlow.Components.Projections.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;

namespace FluxFlow.Components.Projections.Composition;

public static class ProjectionsCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterEventProjection(
        this CompositionNodeRegistry registry,
        string nodeType = ProjectionsCompositionNodeTypes.EventProjection)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateEventProjectionNode,
            inputs:
            [
                CompositionPorts.Metadata<ProjectionEvent>(
                    ProjectionsCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<EventProjectionSnapshot>(
                    ProjectionsCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateEventProjectionNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<EventProjectionOptions>();
        var clock = context.GetResource<TimeProvider>(
            ProjectionsCompositionResourceNames.Clock);
        var node = new EventProjectionNode(options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<ProjectionEvent>(
                    ProjectionsCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<EventProjectionSnapshot>(
                    ProjectionsCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }
}
