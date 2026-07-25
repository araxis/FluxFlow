using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Composition;

namespace FluxFlow.Components.Mqtt.Composition;

public static class MqttCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterMqttNodes(
        this CompositionNodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return registry
            .Register(
                MqttCompositionNodeTypes.ControlDescriptor,
                MqttCompositionNodeFactories.CreateControlNodeAsync,
                inputs:
                [
                    CompositionPorts.Metadata<MqttClientRequest>(MqttCompositionPortNames.Input)
                ],
                outputs:
                [
                    CompositionPorts.Metadata<MqttClientResult>(MqttCompositionPortNames.Output)
                ])
            .Register(
                MqttCompositionNodeTypes.Publish,
                MqttCompositionNodeFactories.CreatePublishNodeAsync,
                inputs:
                [
                    CompositionPorts.Metadata<MqttPublishMessage>(MqttCompositionPortNames.Input)
                ],
                outputs:
                [
                    CompositionPorts.Metadata<MqttClientResult>(MqttCompositionPortNames.Output)
                ])
            .Register(
                MqttCompositionNodeTypes.TriggerDescriptor,
                MqttCompositionNodeFactories.CreateTriggerNodeAsync,
                inputs:
                [
                    CompositionPorts.SignalMetadata(MqttCompositionPortNames.Ack),
                    CompositionPorts.SignalMetadata(MqttCompositionPortNames.Nak)
                ],
                outputs:
                [
                    CompositionPorts.Metadata<MqttReceivedApplicationMessage>(
                        MqttCompositionPortNames.Output)
                ])
            .Register(
                MqttCompositionNodeTypes.Events,
                MqttCompositionNodeFactories.CreateEventsNodeAsync,
                outputs:
                [
                    CompositionPorts.Metadata<MqttClientEvent>(MqttCompositionPortNames.Output)
                ])
            .RegisterResourceTypeAlias(
                MqttCompositionResourceTypes.LegacyRetry,
                MqttCompositionResourceTypes.Retry);
    }
}
