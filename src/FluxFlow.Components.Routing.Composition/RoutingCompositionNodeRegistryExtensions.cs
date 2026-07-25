using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Routing.Composition;

public static class RoutingCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterWindow<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = RoutingCompositionNodeTypes.Window)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateWindowNode<TInput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    RoutingCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowWindow<TInput>>(
                    RoutingCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterWindow(
        this CompositionNodeRegistry registry,
        string nodeType = RoutingCompositionNodeTypes.Window)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateFlowValueWindowNode,
            inputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    RoutingCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<FlowWindow<FlowValue>>>(
                    RoutingCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterCorrelation<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = RoutingCompositionNodeTypes.Correlation)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            RoutingCompositionNodeTypes.CorrelationDescriptor,
            CreateCorrelationNode<TInput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    RoutingCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowCorrelationMatch<TInput>>(
                    RoutingCompositionPortNames.Output),
                CompositionPorts.Metadata<FlowCorrelationMatch<TInput>>(
                    RoutingCompositionPortNames.Matched),
                CompositionPorts.Metadata<FlowCorrelationTimeout<TInput>>(
                    RoutingCompositionPortNames.Timeouts)
            ],
            registrationType: nodeType);
    }

    public static CompositionNodeRegistry RegisterCorrelation(
        this CompositionNodeRegistry registry,
        string nodeType = RoutingCompositionNodeTypes.Correlation)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            RoutingCompositionNodeTypes.CorrelationDescriptor,
            CreateFlowValueCorrelationNode,
            inputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    RoutingCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<FlowCorrelationOutcome<FlowValue>>>(
                    RoutingCompositionPortNames.Output)
            ],
            registrationType: nodeType);
    }

    public static CompositionNodeRegistry RegisterJoin<TLeft, TRight>(
        this CompositionNodeRegistry registry,
        string nodeType = RoutingCompositionNodeTypes.Join)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateJoinNode<TLeft, TRight>,
            inputs:
            [
                CompositionPorts.Metadata<TLeft>(
                    RoutingCompositionPortNames.Left),
                CompositionPorts.Metadata<TRight>(
                    RoutingCompositionPortNames.Right)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowJoinResult<TLeft, TRight>>(
                    RoutingCompositionPortNames.Output),
                CompositionPorts.Metadata<FlowJoinTimeout<TLeft, TRight>>(
                    RoutingCompositionPortNames.Timeouts)
            ]);
    }

    public static CompositionNodeRegistry RegisterJoin(
        this CompositionNodeRegistry registry,
        string nodeType = RoutingCompositionNodeTypes.Join)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateFlowValueJoinNode,
            inputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    RoutingCompositionPortNames.Left),
                CompositionPorts.Metadata<FlowValue>(
                    RoutingCompositionPortNames.Right)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<FlowJoinOutcome<FlowValue, FlowValue>>>(
                    RoutingCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateWindowNode<TInput>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<WindowRoutingOptions>();
        var clock = context.GetResource<TimeProvider>(
            RoutingCompositionResourceNames.Clock);
        var node = new FlowWindowNode<TInput>(options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    RoutingCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowWindow<TInput>>(
                    RoutingCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateFlowValueWindowNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<WindowRoutingOptions>();
        var clock = context.GetResource<TimeProvider>(
            RoutingCompositionResourceNames.Clock);
        var node = new FlowValueWindowNode(options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<FlowValue>(
                    RoutingCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowResult<FlowWindow<FlowValue>>>(
                    RoutingCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComposedNode> CreateCorrelationNode<TInput>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<CorrelationRoutingOptions>();
        var keySelector = context.GetRequiredResource<Func<TInput, string?>>(
            RoutingCompositionResourceNames.KeySelector);
        var sideSelector = context.GetRequiredResource<Func<TInput, string?>>(
            RoutingCompositionResourceNames.SideSelector);
        var clock = context.GetResource<TimeProvider>(
            RoutingCompositionResourceNames.Clock);
        var node = new FlowCorrelationNode<TInput>(
            options,
            keySelector,
            sideSelector,
            options.Engine,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    RoutingCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowCorrelationMatch<TInput>>(
                    RoutingCompositionPortNames.Output,
                    node.Output),
                CompositionPorts.Output<FlowCorrelationMatch<TInput>>(
                    RoutingCompositionPortNames.Matched,
                    node.Matched),
                CompositionPorts.Output<FlowCorrelationTimeout<TInput>>(
                    RoutingCompositionPortNames.Timeouts,
                    node.Timeouts)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateFlowValueCorrelationNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<CorrelationRoutingOptions>();
        var keySelector = context.GetRequiredResource<Func<FlowValue, string?>>(
            RoutingCompositionResourceNames.KeySelector);
        var sideSelector = context.GetRequiredResource<Func<FlowValue, string?>>(
            RoutingCompositionResourceNames.SideSelector);
        var clock = context.GetResource<TimeProvider>(
            RoutingCompositionResourceNames.Clock);
        var node = new FlowValueCorrelationNode(
            options,
            keySelector,
            sideSelector,
            options.Engine,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<FlowValue>(
                    RoutingCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowResult<FlowCorrelationOutcome<FlowValue>>>(
                    RoutingCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComposedNode> CreateJoinNode<TLeft, TRight>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<JoinRoutingOptions>();
        var leftSelector = context.GetRequiredResource<Func<TLeft, string?>>(
            RoutingCompositionResourceNames.LeftKeySelector);
        var rightSelector = context.GetRequiredResource<Func<TRight, string?>>(
            RoutingCompositionResourceNames.RightKeySelector);
        var clock = context.GetResource<TimeProvider>(
            RoutingCompositionResourceNames.Clock);
        var node = new FlowJoinNode<TLeft, TRight>(
            options,
            leftSelector,
            rightSelector,
            options.Engine,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TLeft>(
                    RoutingCompositionPortNames.Left,
                    node.Left),
                CompositionPorts.Input<TRight>(
                    RoutingCompositionPortNames.Right,
                    node.Right)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowJoinResult<TLeft, TRight>>(
                    RoutingCompositionPortNames.Output,
                    node.Output),
                CompositionPorts.Output<FlowJoinTimeout<TLeft, TRight>>(
                    RoutingCompositionPortNames.Timeouts,
                    node.Timeouts)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateFlowValueJoinNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<JoinRoutingOptions>();
        var leftSelector = context.GetRequiredResource<Func<FlowValue, string?>>(
            RoutingCompositionResourceNames.LeftKeySelector);
        var rightSelector = context.GetRequiredResource<Func<FlowValue, string?>>(
            RoutingCompositionResourceNames.RightKeySelector);
        var clock = context.GetResource<TimeProvider>(
            RoutingCompositionResourceNames.Clock);
        var node = new FlowValueJoinNode(
            options,
            leftSelector,
            rightSelector,
            options.Engine,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<FlowValue>(
                    RoutingCompositionPortNames.Left,
                    node.Left),
                CompositionPorts.Input<FlowValue>(
                    RoutingCompositionPortNames.Right,
                    node.Right)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowResult<FlowJoinOutcome<FlowValue, FlowValue>>>(
                    RoutingCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

}
