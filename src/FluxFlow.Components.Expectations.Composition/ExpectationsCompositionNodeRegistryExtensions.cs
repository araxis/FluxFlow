using FluxFlow.Components.Expectations.Contracts;
using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;

namespace FluxFlow.Components.Expectations.Composition;

public static class ExpectationsCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterEventExpectation(
        this CompositionNodeRegistry registry,
        string nodeType = ExpectationsCompositionNodeTypes.EventExpectation)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateEventExpectationNode,
            inputs:
            [
                CompositionPorts.Metadata<ProjectionEvent>(
                    ExpectationsCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<EventExpectationResult>(
                    ExpectationsCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateEventExpectationNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<EventExpectationOptions>();
        var clock = context.GetResource<TimeProvider>(
            ExpectationsCompositionResourceNames.Clock);
        var node = new EventExpectationNode(options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<ProjectionEvent>(
                    ExpectationsCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<EventExpectationResult>(
                    ExpectationsCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }
}
