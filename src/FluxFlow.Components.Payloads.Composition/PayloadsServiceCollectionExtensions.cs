using FluxFlow.Components.Designer;
using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Nodes;
using FluxFlow.Components.Payloads.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Payloads.Composition;

public static class PayloadsServiceCollectionExtensions
{
    internal static ComponentDescriptor InspectDescriptor { get; } = new(
        PayloadsComponentTypes.Inspect,
        CreatePayloadInspectNode,
        inputs:
        [
            ComponentPorts.Metadata<FlowContent>(
                PayloadsComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<PayloadInspectionResult>(
                PayloadsComponentPortNames.Output)
        ]);

    public static IServiceCollection AddPayloadsComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(InspectDescriptor);
        services.AddComponentDesignMetadataProvider<PayloadsComponentDesignMetadataProvider>();
        return services;
    }

    private static ValueTask<ComponentInstance> CreatePayloadInspectNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<PayloadInspectOptions>();
        var clock = context.GetResource<TimeProvider>(
            PayloadsComponentResourceNames.Clock);
        var node = new PayloadInspectNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<FlowContent>(
                    PayloadsComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<PayloadInspectionResult>(
                    PayloadsComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
