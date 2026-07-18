using FluxFlow.Data;
using System.Text.Json.Serialization;

namespace FluxFlow.Components.Mqtt.Events;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "Kind")]
[JsonDerivedType(typeof(MqttClientConnectedEvent), "Connected")]
[JsonDerivedType(typeof(MqttClientDisconnectedEvent), "Disconnected")]
[JsonDerivedType(typeof(MqttSubscriptionChangedEvent), "SubscriptionChanged")]
[JsonDerivedType(typeof(MqttReconnectScheduledEvent), "ReconnectScheduled")]
public abstract record MqttClientEvent
{
    protected MqttClientEvent(string kind, string client, DateTimeOffset timestamp)
    {
        Kind = kind;
        Client = client;
        Timestamp = timestamp;
    }

    [JsonIgnore]
    public string Kind { get; }

    public string Client { get; }

    public DateTimeOffset Timestamp { get; }
}

public sealed record MqttClientConnectedEvent : MqttClientEvent
{
    public MqttClientConnectedEvent(string client, DateTimeOffset timestamp, bool automatic)
        : base("Connected", client, timestamp)
    {
        Automatic = automatic;
    }

    public bool Automatic { get; }
}

public sealed record MqttClientDisconnectedEvent : MqttClientEvent
{
    public MqttClientDisconnectedEvent(
        string client,
        DateTimeOffset timestamp,
        bool expected,
        FlowError? error = null)
        : base("Disconnected", client, timestamp)
    {
        Expected = expected;
        Error = error;
    }

    public bool Expected { get; }

    public FlowError? Error { get; }
}

public sealed record MqttSubscriptionChangedEvent : MqttClientEvent
{
    public MqttSubscriptionChangedEvent(
        string client,
        DateTimeOffset timestamp,
        string name,
        bool subscribed)
        : base("SubscriptionChanged", client, timestamp)
    {
        Name = name;
        Subscribed = subscribed;
    }

    public string Name { get; }

    public bool Subscribed { get; }
}

public sealed record MqttReconnectScheduledEvent : MqttClientEvent
{
    public MqttReconnectScheduledEvent(
        string client,
        DateTimeOffset timestamp,
        int attempt,
        TimeSpan delay)
        : base("ReconnectScheduled", client, timestamp)
    {
        Attempt = attempt;
        Delay = delay;
    }

    public int Attempt { get; }

    public TimeSpan Delay { get; }
}
