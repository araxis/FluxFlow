using FluxFlow.Components.Designer;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Mqtt.Composition;

public static class MqttServiceCollectionExtensions
{
    internal static ComponentDescriptor ControlDescriptor { get; } = new(
        MqttComponentTypes.Control,
        MqttCompositionNodeFactories.CreateControlNodeAsync,
        inputs: [ComponentPorts.Metadata<MqttClientRequest>(MqttComponentPortNames.Input)],
        outputs: [ComponentPorts.Metadata<MqttClientResult>(MqttComponentPortNames.Output)]);
    internal static ComponentDescriptor PublishDescriptor { get; } = new(
        MqttComponentTypes.Publish,
        MqttCompositionNodeFactories.CreatePublishNodeAsync,
        inputs: [ComponentPorts.Metadata<MqttPublishMessage>(MqttComponentPortNames.Input)],
        outputs: [ComponentPorts.Metadata<MqttClientResult>(MqttComponentPortNames.Output)]);
    internal static ComponentDescriptor TriggerDescriptor { get; } = new(
        MqttComponentTypes.Trigger,
        MqttCompositionNodeFactories.CreateTriggerNodeAsync,
        inputs:
        [
            ComponentPorts.SignalMetadata(MqttComponentPortNames.Ack),
            ComponentPorts.SignalMetadata(MqttComponentPortNames.Nak)
        ],
        outputs:
        [
            ComponentPorts.Metadata<MqttReceivedApplicationMessage>(MqttComponentPortNames.Output)
        ]);
    internal static ComponentDescriptor EventsDescriptor { get; } = new(
        MqttComponentTypes.Events,
        MqttCompositionNodeFactories.CreateEventsNodeAsync,
        outputs: [ComponentPorts.Metadata<MqttClientEvent>(MqttComponentPortNames.Output)]);

    public static IServiceCollection AddMqttComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(ControlDescriptor);
        services.AddFluxFlowComponent(PublishDescriptor);
        services.AddFluxFlowComponent(TriggerDescriptor);
        services.AddFluxFlowComponent(EventsDescriptor);
        services.AddComponentDesignMetadataProvider<MqttComponentDesignMetadataProvider>();
        services.AddApplicationResourceRegistrar<MqttCompositionResourceRegistrar>();
        return services;
    }
}
