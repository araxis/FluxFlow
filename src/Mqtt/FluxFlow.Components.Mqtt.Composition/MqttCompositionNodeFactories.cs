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

    internal static async ValueTask<MqttControlNode> CreateControlNodeAsync(
        ComponentActivationContext context)
    {
        var controller = await GetStartedControllerAsync(context).ConfigureAwait(false);
        return new MqttControlNode(controller, context.BindConfiguration<MqttControlOptions>());
    }

    internal static async ValueTask<MqttPublishOperationNode> CreatePublishNodeAsync(
        ComponentActivationContext context)
    {
        var controller = await GetStartedControllerAsync(context).ConfigureAwait(false);
        var options = context.BindConfiguration<MqttPublishCompositionOptions>();
        return new MqttPublishOperationNode(controller, options.MaximumPendingRequests);
    }

    internal static async ValueTask<MqttSubscriptionTriggerNode> CreateTriggerNodeAsync(
        ComponentActivationContext context)
    {
        var controller = await GetStartedControllerAsync(context).ConfigureAwait(false);
        var options = context.BindConfiguration<MqttTriggerCompositionOptions>();
        return new MqttSubscriptionTriggerNode(
            controller,
            BindTriggerOptions(context, options),
            context.GetResource<TimeProvider>(MqttComponentDefinition.Resources.Clock));
    }

    internal static async ValueTask<MqttClientEventsNode> CreateEventsNodeAsync(
        ComponentActivationContext context)
    {
        var controller = await GetStartedControllerAsync(context).ConfigureAwait(false);
        var options = context.BindConfiguration<MqttEventsCompositionOptions>();
        return new MqttClientEventsNode(controller, options.MaximumPendingEvents);
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
