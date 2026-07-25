using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Nodes;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Mqtt.Composition;

internal static class MqttCompositionNodeFactories
{
    internal static async ValueTask<ComposedNode> CreateControlNodeAsync(
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

    internal static async ValueTask<ComposedNode> CreatePublishNodeAsync(
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

    internal static async ValueTask<ComposedNode> CreateTriggerNodeAsync(
        CompositionNodeFactoryContext context)
    {
        var controller = await GetStartedControllerAsync(context).ConfigureAwait(false);
        var options = context.BindConfiguration<MqttTriggerCompositionOptions>();
        var node = new MqttSubscriptionTriggerNode(
            controller,
            BindTriggerOptions(context, options),
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

    internal static async ValueTask<ComposedNode> CreateEventsNodeAsync(
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
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        var properties = JsonSerializer.SerializeToElement(binding, options)
            .EnumerateObject()
            .ToDictionary(
                static property => property.Name,
                static property => property.Value,
                StringComparer.Ordinal);
        properties["TriggerId"] = JsonSerializer.SerializeToElement(
            $"{context.WorkflowName}.{context.ComponentName}");
        return JsonSerializer.Deserialize<MqttSubscriptionTriggerOptions>(
                   JsonSerializer.Serialize(properties, options),
                   options)
               ?? throw new InvalidOperationException(
                   $"Configuration for MQTT trigger '{context.WorkflowName}.{context.ComponentName}' is invalid.");
    }
}
