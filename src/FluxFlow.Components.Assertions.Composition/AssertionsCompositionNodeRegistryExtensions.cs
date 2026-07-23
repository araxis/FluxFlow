using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Nodes;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Assertions.Composition;

public static class AssertionsCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterAssertion(
        this CompositionNodeRegistry registry,
        string nodeType = AssertionsCompositionNodeTypes.Assert)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            AssertionsCompositionNodeTypes.AssertDescriptor,
            CreateFlowValueAssertionNode,
            inputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    AssertionsCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<FlowValueAssertionResult>>(
                    AssertionsCompositionPortNames.Output)
            ],
            registrationType: nodeType);
    }

    private static ValueTask<ComposedNode> CreateFlowValueAssertionNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<FlowValueAssertionOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            AssertionsCompositionResourceNames.Engine);
        var contextFactory = context.GetResource<IFlowMapContextFactory<FlowValue>>(
            AssertionsCompositionResourceNames.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            AssertionsCompositionResourceNames.Clock);
        var node = new FlowValueAssertionNode(
            options,
            expressionEngine,
            contextFactory,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<FlowValue>(
                    AssertionsCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowResult<FlowValueAssertionResult>>(
                    AssertionsCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
