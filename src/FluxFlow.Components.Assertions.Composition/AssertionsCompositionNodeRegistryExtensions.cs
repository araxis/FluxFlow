using System.Text.Json;
using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Nodes;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Composition;
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
            CreateJsonAssertionNode,
            inputs:
            [
                CompositionPorts.Metadata<JsonElement>(
                    AssertionsCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<AssertionResult<JsonElement>>(
                    AssertionsCompositionPortNames.Output)
            ],
            registrationType: nodeType);
    }

    private static ValueTask<ComposedNode> CreateJsonAssertionNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<AssertionOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            AssertionsCompositionResourceNames.Engine);
        var contextFactory = context.GetResource<IFlowMapContextFactory<JsonElement>>(
            AssertionsCompositionResourceNames.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            AssertionsCompositionResourceNames.Clock);
        var node = new JsonAssertionNode(
            options,
            expressionEngine,
            contextFactory,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<JsonElement>(
                    AssertionsCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<AssertionResult<JsonElement>>(
                    AssertionsCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
