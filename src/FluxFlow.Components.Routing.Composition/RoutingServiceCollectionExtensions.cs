using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Routing.Composition;

public static class RoutingServiceCollectionExtensions
{
    internal static ComponentDescriptor WindowDescriptor { get; } = new(
        RoutingComponentTypes.Window,
        CreateJsonWindowNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(RoutingComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<FlowWindow<JsonElement>>(RoutingComponentPortNames.Output)
        ]);

    internal static ComponentDescriptor CorrelationDescriptor { get; } = new(
        RoutingComponentTypes.Correlation,
        CreateJsonCorrelationNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(RoutingComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<FlowCorrelationOutcome<JsonElement>>(RoutingComponentPortNames.Output)
        ]);

    internal static ComponentDescriptor JoinDescriptor { get; } = new(
        RoutingComponentTypes.Join,
        CreateJsonJoinNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(RoutingComponentPortNames.Left),
            ComponentPorts.Metadata<JsonElement>(RoutingComponentPortNames.Right)
        ],
        outputs:
        [
            ComponentPorts.Metadata<FlowJoinOutcome<JsonElement, JsonElement>>(
                RoutingComponentPortNames.Output)
        ]);

    public static IServiceCollection AddRoutingComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(WindowDescriptor);
        services.AddFluxFlowComponent(CorrelationDescriptor);
        services.AddFluxFlowComponent(JoinDescriptor);
        services.AddComponentDesignMetadataProvider<RoutingComponentDesignMetadataProvider>();
        return services;
    }

    private static ValueTask<ComponentInstance> CreateJsonWindowNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<WindowRoutingOptions>();
        var clock = context.GetResource<TimeProvider>(
            RoutingComponentResourceNames.Clock);
        var node = new JsonWindowNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    RoutingComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<FlowWindow<JsonElement>>(
                    RoutingComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateJsonCorrelationNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<CorrelationRoutingOptions>();
        var keySelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingComponentResourceNames.KeySelector);
        var sideSelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingComponentResourceNames.SideSelector);
        var clock = context.GetResource<TimeProvider>(
            RoutingComponentResourceNames.Clock);
        var node = new JsonCorrelationNode(
            options,
            keySelector,
            sideSelector,
            options.Engine,
            clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    RoutingComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<FlowCorrelationOutcome<JsonElement>>(
                    RoutingComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateJsonJoinNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<JoinRoutingOptions>();
        var leftSelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingComponentResourceNames.LeftKeySelector);
        var rightSelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingComponentResourceNames.RightKeySelector);
        var clock = context.GetResource<TimeProvider>(
            RoutingComponentResourceNames.Clock);
        var node = new JsonJoinNode(
            options,
            leftSelector,
            rightSelector,
            options.Engine,
            clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    RoutingComponentPortNames.Left,
                    node.Left),
                ComponentPorts.Input<JsonElement>(
                    RoutingComponentPortNames.Right,
                    node.Right)
            ],
            outputs:
            [
                ComponentPorts.Output<FlowJoinOutcome<JsonElement, JsonElement>>(
                    RoutingComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

}
