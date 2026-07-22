using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Nodes;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Composition;
using System.Text.Json;

namespace FluxFlow.Components.Mqtt.Composition;

public static class MqttCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterMqttNodes(
        this CompositionNodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return registry
            .Register(
                MqttCompositionNodeTypes.Control,
                CreateControlNodeAsync,
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
                CreatePublishNodeAsync,
                inputs:
                [
                    CompositionPorts.Metadata<MqttPublishMessage>(MqttCompositionPortNames.Input)
                ],
                outputs:
                [
                    CompositionPorts.Metadata<MqttClientResult>(MqttCompositionPortNames.Output)
                ])
            .Register(
                MqttCompositionNodeTypes.Trigger,
                CreateTriggerNodeAsync,
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
                CreateEventsNodeAsync,
                outputs:
                [
                    CompositionPorts.Metadata<MqttClientEvent>(MqttCompositionPortNames.Output)
                ])
            .RegisterAlias(
                MqttCompositionNodeTypes.LegacyControl,
                MqttCompositionNodeTypes.Control)
            .RegisterAlias(
                MqttCompositionNodeTypes.LegacyTrigger,
                MqttCompositionNodeTypes.Trigger);
    }

    private static async ValueTask<ComposedNode> CreateControlNodeAsync(
        CompositionNodeFactoryContext context)
    {
        var controller = await GetStartedControllerAsync(context).ConfigureAwait(false);
        var node = new MqttControlNode(controller, context.BindConfiguration<MqttControlOptions>());
        return ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<MqttClientRequest>(
                    MqttCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<MqttClientResult>(
                    MqttCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events);
    }

    private static async ValueTask<ComposedNode> CreatePublishNodeAsync(
        CompositionNodeFactoryContext context)
    {
        var controller = await GetStartedControllerAsync(context).ConfigureAwait(false);
        var options = context.BindConfiguration<MqttPublishCompositionOptions>();
        var node = new MqttPublishOperationNode(controller, options.MaximumPendingRequests);
        return ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<MqttPublishMessage>(
                    MqttCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<MqttClientResult>(
                    MqttCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events);
    }

    private static async ValueTask<ComposedNode> CreateTriggerNodeAsync(
        CompositionNodeFactoryContext context)
    {
        var controller = await GetStartedControllerAsync(context).ConfigureAwait(false);
        var options = context.BindConfiguration<MqttTriggerCompositionOptions>();
        var binding = BindTriggerOptions(context, options);
        var node = new MqttSubscriptionTriggerNode(
            controller,
            binding,
            context.GetResource<TimeProvider>(MqttCompositionResourceNames.Clock));
        return ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.SignalInput(MqttCompositionPortNames.Ack, node.Ack),
                CompositionPorts.SignalInput(MqttCompositionPortNames.Nak, node.Nak)
            ],
            outputs:
            [
                CompositionPorts.Output<MqttReceivedApplicationMessage>(
                    MqttCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events);
    }

    private static async ValueTask<ComposedNode> CreateEventsNodeAsync(
        CompositionNodeFactoryContext context)
    {
        var controller = await GetStartedControllerAsync(context).ConfigureAwait(false);
        var options = context.BindConfiguration<MqttEventsCompositionOptions>();
        var node = new MqttClientEventsNode(controller, options.MaximumPendingEvents);
        return ComposedNode.Create(
            node,
            outputs:
            [
                CompositionPorts.Output<MqttClientEvent>(
                    MqttCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events);
    }

    private static async ValueTask<IMqttClientController> GetStartedControllerAsync(
        CompositionNodeFactoryContext context)
    {
        var controller = context.GetRequiredResource<IMqttClientController>(
            MqttCompositionResourceNames.Client);
        await controller.StartAsync().ConfigureAwait(false);
        return controller;
    }

    private static MqttSubscriptionTriggerOptions BindTriggerOptions(
        CompositionNodeFactoryContext context,
        MqttTriggerCompositionOptions binding)
    {
        var options = CompositionDefinitionJson.CreateSerializerOptions();
        var properties = JsonSerializer.SerializeToElement(binding, options)
            .EnumerateObject()
            .ToDictionary(
                static property => property.Name,
                static property => property.Value,
                StringComparer.Ordinal);
        properties["TriggerId"] = JsonSerializer.SerializeToElement(
            $"{context.WorkflowName}.{context.NodeName}");
        return JsonSerializer.Deserialize<MqttSubscriptionTriggerOptions>(
                   JsonSerializer.Serialize(properties, options),
                   options)
               ?? throw new InvalidOperationException(
                   $"Configuration for MQTT trigger '{context.WorkflowName}.{context.NodeName}' is invalid.");
    }
}
