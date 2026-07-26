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
    internal static async ValueTask<ComponentInstance> CreateControlNodeAsync(
        ComponentActivationContext context)
    {
        var controller = await GetStartedControllerAsync(context).ConfigureAwait(false);
        var node = new MqttControlNode(controller, context.BindConfiguration<MqttControlOptions>());
        return ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<MqttClientRequest>(
                    MqttComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<MqttClientResult>(
                    MqttComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events);
    }

    internal static async ValueTask<ComponentInstance> CreatePublishNodeAsync(
        ComponentActivationContext context)
    {
        var controller = await GetStartedControllerAsync(context).ConfigureAwait(false);
        var options = context.BindConfiguration<MqttPublishCompositionOptions>();
        var node = new MqttPublishOperationNode(controller, options.MaximumPendingRequests);
        return ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<MqttPublishMessage>(
                    MqttComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<MqttClientResult>(
                    MqttComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events);
    }

    internal static async ValueTask<ComponentInstance> CreateTriggerNodeAsync(
        ComponentActivationContext context)
    {
        var controller = await GetStartedControllerAsync(context).ConfigureAwait(false);
        var options = context.BindConfiguration<MqttTriggerCompositionOptions>();
        var node = new MqttSubscriptionTriggerNode(
            controller,
            BindTriggerOptions(context, options),
            context.GetResource<TimeProvider>(MqttComponentResourceNames.Clock));
        return ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.SignalInput(MqttComponentPortNames.Ack, node.Ack),
                ComponentPorts.SignalInput(MqttComponentPortNames.Nak, node.Nak)
            ],
            outputs:
            [
                ComponentPorts.Output<MqttReceivedApplicationMessage>(
                    MqttComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events);
    }

    internal static async ValueTask<ComponentInstance> CreateEventsNodeAsync(
        ComponentActivationContext context)
    {
        var controller = await GetStartedControllerAsync(context).ConfigureAwait(false);
        var options = context.BindConfiguration<MqttEventsCompositionOptions>();
        var node = new MqttClientEventsNode(controller, options.MaximumPendingEvents);
        return ComponentInstance.Create(
            node,
            outputs:
            [
                ComponentPorts.Output<MqttClientEvent>(
                    MqttComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events);
    }

    private static async ValueTask<IMqttClientController> GetStartedControllerAsync(
        ComponentActivationContext context)
    {
        var controller = context.GetRequiredResource<IMqttClientController>(
            MqttComponentResourceNames.Client);
        await controller.StartAsync().ConfigureAwait(false);
        return controller;
    }

    private static MqttSubscriptionTriggerOptions BindTriggerOptions(
        ComponentActivationContext context,
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
