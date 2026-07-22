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

        return RegisterLegacyAlias(registry.Register(
            nodeType,
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
            ]), nodeType);
    }

    public static CompositionNodeRegistry RegisterAssertion<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = AssertionsCompositionNodeTypes.Assert)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return RegisterLegacyAlias(registry.Register(
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
            ]), nodeType);
    }

    private static CompositionNodeRegistry RegisterLegacyAlias(
        CompositionNodeRegistry registry,
        string nodeType)
    {
        if (string.Equals(nodeType, AssertionsCompositionNodeTypes.Assert, StringComparison.Ordinal))
        {
            registry.RegisterAlias(
                AssertionsCompositionNodeTypes.LegacyAssert,
                AssertionsCompositionNodeTypes.Assert);
        }

        return registry;
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
