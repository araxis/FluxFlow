using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Subscriptions;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace FluxFlow.Components.Mqtt.Options;

public sealed record MqttSubscriptionTriggerOptions
{
    private IReadOnlyList<MqttSubscriptionTarget> _subscriptions =
        ImmutableArray<MqttSubscriptionTarget>.Empty;

    public required string TriggerId { get; init; }

    [JsonPropertyName("Subscription")]
    [JsonConverter(typeof(MqttSubscriptionListJsonConverter))]
    public IReadOnlyList<MqttSubscriptionTarget> Subscriptions
    {
        get => _subscriptions;
        init => _subscriptions = value is null || value.Count == 0
            ? ImmutableArray<MqttSubscriptionTarget>.Empty
            : value.ToImmutableArray();
    }

    public MqttWorkflowAcknowledgement WorkflowAcknowledgement { get; init; }

    public MqttBrokerAcknowledgement BrokerAcknowledgement { get; init; }

    public TimeSpan OutcomeTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public int MaximumPendingMessages { get; init; } = 128;
}
