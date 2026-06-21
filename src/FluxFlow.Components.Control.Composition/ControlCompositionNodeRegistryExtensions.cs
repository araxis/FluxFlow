using FluxFlow.Components.Control.Nodes;
using FluxFlow.Components.Control.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Control.Composition;

public static class ControlCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterFilter<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = ControlCompositionNodeTypes.Filter)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateFilterNode<TInput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    ControlCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<TInput>(
                    ControlCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterWhen<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = ControlCompositionNodeTypes.When)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateWhenNode<TInput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    ControlCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<TInput>(
                    ControlCompositionPortNames.WhenTrue),
                CompositionPorts.Metadata<TInput>(
                    ControlCompositionPortNames.WhenFalse),
                CompositionPorts.Metadata<TInput>(
                    ControlCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateFilterNode<TInput>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<ControlExpressionOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            ControlCompositionResourceNames.Engine);
        var contextFactory = context.GetResource<IFlowMapContextFactory<TInput>>(
            ControlCompositionResourceNames.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            ControlCompositionResourceNames.Clock);
        var node = new FilterNode<TInput>(
            options,
            expressionEngine,
            contextFactory,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    ControlCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<TInput>(
                    ControlCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateWhenNode<TInput>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<ControlExpressionOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            ControlCompositionResourceNames.Engine);
        var contextFactory = context.GetResource<IFlowMapContextFactory<TInput>>(
            ControlCompositionResourceNames.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            ControlCompositionResourceNames.Clock);
        var node = new WhenNode<TInput>(
            options,
            expressionEngine,
            contextFactory,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    ControlCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<TInput>(
                    ControlCompositionPortNames.WhenTrue,
                    node.WhenTrue),
                CompositionPorts.Output<TInput>(
                    ControlCompositionPortNames.WhenFalse,
                    node.WhenFalse),
                CompositionPorts.Output<TInput>(
                    ControlCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }
}
