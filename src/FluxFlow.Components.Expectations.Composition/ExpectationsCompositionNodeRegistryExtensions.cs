using FluxFlow.Components.Expectations.Contracts;
using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Composition;
using FluxFlow.Data;

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
            ExpectationsCompositionNodeTypes.EventExpectationDescriptor,
            CreateEventExpectationNode,
            inputs:
            [
                CompositionPorts.Metadata<ProjectionEvent>(
                    ExpectationsCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<EventExpectationResult>>(
                    ExpectationsCompositionPortNames.Output)
            ],
            registrationType: nodeType);
    }

    private static ValueTask<ComposedNode> CreateEventExpectationNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<EventExpectationOptions>();
        var clock = context.GetResource<TimeProvider>(
            ExpectationsCompositionResourceNames.Clock);
        var node = new FlowEventExpectationNode(options, clock);

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
                CompositionPorts.Output<FlowResult<EventExpectationResult>>(
                    ExpectationsCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

}
