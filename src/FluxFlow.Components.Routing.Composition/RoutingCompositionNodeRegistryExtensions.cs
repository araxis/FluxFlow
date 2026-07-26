using System.Text.Json;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Composition;

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
            CreateJsonWindowNode,
            inputs:
            [
                CompositionPorts.Metadata<JsonElement>(
                    RoutingCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowWindow<JsonElement>>(
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
            CreateJsonCorrelationNode,
            inputs:
            [
                CompositionPorts.Metadata<JsonElement>(
                    RoutingCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowCorrelationOutcome<JsonElement>>(
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
            CreateJsonJoinNode,
            inputs:
            [
                CompositionPorts.Metadata<JsonElement>(
                    RoutingCompositionPortNames.Left),
                CompositionPorts.Metadata<JsonElement>(
                    RoutingCompositionPortNames.Right)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowJoinOutcome<JsonElement, JsonElement>>(
                    RoutingCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateJsonWindowNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<WindowRoutingOptions>();
        var clock = context.GetResource<TimeProvider>(
            RoutingCompositionResourceNames.Clock);
        var node = new JsonWindowNode(options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<JsonElement>(
                    RoutingCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowWindow<JsonElement>>(
                    RoutingCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComposedNode> CreateJsonCorrelationNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<CorrelationRoutingOptions>();
        var keySelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingCompositionResourceNames.KeySelector);
        var sideSelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingCompositionResourceNames.SideSelector);
        var clock = context.GetResource<TimeProvider>(
            RoutingCompositionResourceNames.Clock);
        var node = new JsonCorrelationNode(
            options,
            keySelector,
            sideSelector,
            options.Engine,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<JsonElement>(
                    RoutingCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowCorrelationOutcome<JsonElement>>(
                    RoutingCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComposedNode> CreateJsonJoinNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<JoinRoutingOptions>();
        var leftSelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingCompositionResourceNames.LeftKeySelector);
        var rightSelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingCompositionResourceNames.RightKeySelector);
        var clock = context.GetResource<TimeProvider>(
            RoutingCompositionResourceNames.Clock);
        var node = new JsonJoinNode(
            options,
            leftSelector,
            rightSelector,
            options.Engine,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<JsonElement>(
                    RoutingCompositionPortNames.Left,
                    node.Left),
                CompositionPorts.Input<JsonElement>(
                    RoutingCompositionPortNames.Right,
                    node.Right)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowJoinOutcome<JsonElement, JsonElement>>(
                    RoutingCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

}
