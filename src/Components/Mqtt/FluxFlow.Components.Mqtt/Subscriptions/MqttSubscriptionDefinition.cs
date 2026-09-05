using FluxFlow.Components.Mqtt.Contracts;
using System.Text.Json.Serialization;

namespace FluxFlow.Components.Mqtt.Subscriptions;

public sealed record MqttSubscriptionDefinition
{
    public required string TopicFilter { get; init; }

    public MqttQos Qos { get; init; }

    public bool NoLocal { get; init; }

    public bool RetainAsPublished { get; init; }

    public MqttRetainHandling RetainHandling { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<MqttRetainHandling>))]
public enum MqttRetainHandling
{
    SendOnSubscribe = 0,
    SendOnNewSubscription = 1,
    DoNotSend = 2
}
