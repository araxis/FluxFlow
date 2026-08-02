using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Nodes;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Composition;

namespace FluxFlow.Components.Mqtt.Composition;

internal static class MqttCompositionNodeFactories
{
    private static readonly JsonSerializerOptions TriggerSubscriptionSerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

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
                    MqttComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<MqttClientResult>(
                    MqttComponentDefinition.Ports.Output,
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
                    MqttComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<MqttClientResult>(
                    MqttComponentDefinition.Ports.Output,
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
            context.GetResource<TimeProvider>(MqttComponentDefinition.Resources.Clock));
        return ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.SignalInput(MqttComponentDefinition.Ports.Ack, node.Ack),
                ComponentPorts.SignalInput(MqttComponentDefinition.Ports.Nak, node.Nak)
            ],
            outputs:
            [
                ComponentPorts.Output<MqttReceivedApplicationMessage>(
                    MqttComponentDefinition.Ports.Output,
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
                    MqttComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events);
    }

    private static async ValueTask<IMqttClientController> GetStartedControllerAsync(
        ComponentActivationContext context)
    {
        var controller = context.GetRequiredResource<IMqttClientController>(
            MqttComponentDefinition.Resources.Client);
        await controller.StartAsync().ConfigureAwait(false);
        return controller;
    }

    private static MqttSubscriptionTriggerOptions BindTriggerOptions(
        ComponentActivationContext context,
        MqttTriggerCompositionOptions binding)
        => new()
        {
            TriggerId = $"{context.WorkflowName}.{context.ComponentName}",
            Subscriptions = BindSubscriptions(binding.Subscription),
            WorkflowAcknowledgement = binding.WorkflowAcknowledgement,
            BrokerAcknowledgement = binding.BrokerAcknowledgement,
            OutcomeTimeout = binding.OutcomeTimeout,
            MaximumPendingMessages = binding.MaximumPendingMessages
        };

    private static IReadOnlyList<MqttSubscriptionTarget> BindSubscriptions(JsonElement binding)
    {
        if (binding.ValueKind != JsonValueKind.Array)
            return [BindSubscription(binding)];

        return binding
            .EnumerateArray()
            .Select(BindSubscription)
            .ToArray();
    }

    private static MqttSubscriptionTarget BindSubscription(JsonElement binding)
        => binding.Deserialize<MqttSubscriptionTarget>(
               TriggerSubscriptionSerializerOptions)
           ?? throw new JsonException("The MQTT trigger subscription is null.");
}
