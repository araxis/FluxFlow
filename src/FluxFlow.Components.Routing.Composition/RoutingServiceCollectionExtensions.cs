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
    private static readonly Lazy<IReadOnlyCollection<ComponentDesignDeclaration>> DeclarationSet =
        new(CreateDeclarations);

    internal static IReadOnlyCollection<ComponentDesignDeclaration> Declarations =>
        DeclarationSet.Value;

    private static IReadOnlyCollection<ComponentDesignDeclaration> CreateDeclarations() =>
        ComponentDesignDeclaration.CreateRange(
            [
                WindowDescriptor,
                CorrelationDescriptor,
                JoinDescriptor
            ],
            RoutingComponentDefinition.CreateMetadata());

    internal static ComponentDescriptor WindowDescriptor { get; } = new(
        RoutingComponentDefinition.Types.Window,
        CreateJsonWindowNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(RoutingComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<FlowWindow<JsonElement>>(RoutingComponentDefinition.Ports.Output)
        ],
        options: RoutingComponentDefinition.CreateOptions(RoutingComponentDefinition.Types.Window),
        resources: RoutingComponentDefinition.CreateResources(RoutingComponentDefinition.Types.Window));

    internal static ComponentDescriptor CorrelationDescriptor { get; } = new(
        RoutingComponentDefinition.Types.Correlation,
        CreateJsonCorrelationNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(RoutingComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<FlowCorrelationOutcome<JsonElement>>(RoutingComponentDefinition.Ports.Output)
        ],
        options: RoutingComponentDefinition.CreateOptions(RoutingComponentDefinition.Types.Correlation),
        resources: RoutingComponentDefinition.CreateResources(RoutingComponentDefinition.Types.Correlation));

    internal static ComponentDescriptor JoinDescriptor { get; } = new(
        RoutingComponentDefinition.Types.Join,
        CreateJsonJoinNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(RoutingComponentDefinition.Ports.Left),
            ComponentPorts.Metadata<JsonElement>(RoutingComponentDefinition.Ports.Right)
        ],
        outputs:
        [
            ComponentPorts.Metadata<FlowJoinOutcome<JsonElement, JsonElement>>(
                RoutingComponentDefinition.Ports.Output)
        ],
        options: RoutingComponentDefinition.CreateOptions(RoutingComponentDefinition.Types.Join),
        resources: RoutingComponentDefinition.CreateResources(RoutingComponentDefinition.Types.Join));

    public static IServiceCollection AddRoutingComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclarations(Declarations);
        return services;
    }

    private static ValueTask<ComponentInstance> CreateJsonWindowNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<WindowRoutingOptions>();
        var clock = context.GetResource<TimeProvider>(
            RoutingComponentDefinition.Resources.Clock);
        var node = new JsonWindowNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    RoutingComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<FlowWindow<JsonElement>>(
                    RoutingComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateJsonCorrelationNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<CorrelationRoutingOptions>();
        var keySelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingComponentDefinition.Resources.KeySelector);
        var sideSelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingComponentDefinition.Resources.SideSelector);
        var clock = context.GetResource<TimeProvider>(
            RoutingComponentDefinition.Resources.Clock);
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
                    RoutingComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<FlowCorrelationOutcome<JsonElement>>(
                    RoutingComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateJsonJoinNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<JoinRoutingOptions>();
        var leftSelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingComponentDefinition.Resources.LeftKeySelector);
        var rightSelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingComponentDefinition.Resources.RightKeySelector);
        var clock = context.GetResource<TimeProvider>(
            RoutingComponentDefinition.Resources.Clock);
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
                    RoutingComponentDefinition.Ports.Left,
                    node.Left),
                ComponentPorts.Input<JsonElement>(
                    RoutingComponentDefinition.Ports.Right,
                    node.Right)
            ],
            outputs:
            [
                ComponentPorts.Output<FlowJoinOutcome<JsonElement, JsonElement>>(
                    RoutingComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

}
