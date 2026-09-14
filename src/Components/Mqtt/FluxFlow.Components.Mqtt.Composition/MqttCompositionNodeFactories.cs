using FluxFlow.Nodes;
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

    internal static async ValueTask<IFlowNode> CreateControlNodeAsync(
        ComponentActivationContext context)
    {
        var controller = await GetStartedControllerAsync(context).ConfigureAwait(false);
        var node = new MqttControlNode(controller, context.BindConfiguration<MqttControlOptions>());
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return node;
    }

    internal static async ValueTask<IFlowNode> CreatePublishNodeAsync(
        ComponentActivationContext context)
    {
        var controller = await GetStartedControllerAsync(context).ConfigureAwait(false);
        var options = context.BindConfiguration<MqttPublishCompositionOptions>();
        var node = new MqttPublishOperationNode(controller, options.MaximumPendingRequests);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return node;
    }

    internal static async ValueTask<IFlowNode> CreateTriggerNodeAsync(
        ComponentActivationContext context)
    {
        var controller = await GetStartedControllerAsync(context).ConfigureAwait(false);
        var options = context.BindConfiguration<MqttTriggerCompositionOptions>();
        var node = new MqttSubscriptionTriggerNode(
            controller,
            BindTriggerOptions(context, options),
            context.GetResource<TimeProvider>(MqttComponentDefinition.Resources.Clock));
        context.HasInput("Ack", node.Ack, signal: true);
        context.HasInput("Nak", node.Nak, signal: true);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return node;
    }

    internal static async ValueTask<IFlowNode> CreateEventsNodeAsync(
        ComponentActivationContext context)
    {
        var controller = await GetStartedControllerAsync(context).ConfigureAwait(false);
        var options = context.BindConfiguration<MqttEventsCompositionOptions>();
        var node = new MqttClientEventsNode(controller, options.MaximumPendingEvents);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return node;
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
