using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Nodes;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Assertions.Composition;

public static class AssertionsCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterAssertion<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = AssertionsCompositionNodeTypes.Assert)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateAssertionNode<TInput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    AssertionsCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowAssertionResult>(
                    AssertionsCompositionPortNames.Output),
                CompositionPorts.Metadata<TInput>(
                    AssertionsCompositionPortNames.Passed),
                CompositionPorts.Metadata<TInput>(
                    AssertionsCompositionPortNames.Failed)
            ]);
    }

    private static ValueTask<ComposedNode> CreateAssertionNode<TInput>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<AssertionOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            AssertionsCompositionResourceNames.Engine);
        var contextFactory = context.GetResource<IFlowMapContextFactory<TInput>>(
            AssertionsCompositionResourceNames.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            AssertionsCompositionResourceNames.Clock);
        var node = new FlowAssertionComponent<TInput>(
            options,
            expressionEngine,
            contextFactory,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    AssertionsCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowAssertionResult>(
                    AssertionsCompositionPortNames.Output,
                    node.Output),
                CompositionPorts.Output<TInput>(
                    AssertionsCompositionPortNames.Passed,
                    node.Passed),
                CompositionPorts.Output<TInput>(
                    AssertionsCompositionPortNames.Failed,
                    node.Failed)
            ],
            events: node.Events,
            errors: node.Errors));
    }
}
