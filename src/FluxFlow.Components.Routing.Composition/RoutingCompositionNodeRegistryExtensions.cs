using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Routing.Composition;

public static class RoutingCompositionNodeRegistryExtensions
{
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
