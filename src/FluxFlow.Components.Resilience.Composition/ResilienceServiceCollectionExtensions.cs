using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Resilience.Contracts;
using FluxFlow.Components.Resilience.Nodes;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Composition;
using FluxFlow.Resilience;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Resilience.Composition;

public static class ResilienceServiceCollectionExtensions
{
    internal static ComponentDescriptor RetryDescriptor { get; } = new(
        ResilienceComponentTypes.Retry,
        CreateFlowRetryNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(ResilienceComponentPortNames.Input),
            ComponentPorts.SignalMetadata(ResilienceComponentPortNames.Ack),
            ComponentPorts.SignalMetadata(ResilienceComponentPortNames.Nak),
            ComponentPorts.SignalMetadata(ResilienceComponentPortNames.Cancel)
        ],
        outputs:
        [
            ComponentPorts.Metadata<RetrySignal<JsonElement>>(
                ResilienceComponentPortNames.Output)
        ]);

    public static IServiceCollection AddResilienceComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(RetryDescriptor);
        services.AddComponentDesignMetadataProvider<ResilienceComponentDesignMetadataProvider>();
        return services;
    }

    private static ValueTask<ComponentInstance> CreateFlowRetryNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FlowRetryOptions>();
        if (string.IsNullOrWhiteSpace(options.Name))
        {
            options = options with
            {
                Name = $"{context.WorkflowName}.{context.ComponentName}"
            };
        }

        var node = new FlowRetryNode(
            options,
            context.GetResource<TimeProvider>(ResilienceComponentResourceNames.Clock),
            context.GetResource<IRetryJitterSource>(ResilienceComponentResourceNames.Jitter));
        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    ResilienceComponentPortNames.Input,
                    node.Input),
                ComponentPorts.SignalInput(
                    ResilienceComponentPortNames.Ack,
                    node.Ack),
                ComponentPorts.SignalInput(
                    ResilienceComponentPortNames.Nak,
                    node.Nak),
                ComponentPorts.SignalInput(
                    ResilienceComponentPortNames.Cancel,
                    node.Cancel)
            ],
            outputs:
            [
                ComponentPorts.Output<RetrySignal<JsonElement>>(
                    ResilienceComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
