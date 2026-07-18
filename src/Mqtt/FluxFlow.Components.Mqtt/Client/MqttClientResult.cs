using FluxFlow.Data;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Subscriptions;
using System.Text.Json.Serialization;

namespace FluxFlow.Components.Mqtt.Client;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "Kind")]
[JsonDerivedType(typeof(MqttConnectResult), "Connect")]
[JsonDerivedType(typeof(MqttDisconnectResult), "Disconnect")]
[JsonDerivedType(typeof(MqttStatusResult), "Status")]
[JsonDerivedType(typeof(MqttPublishOperationResult), "Publish")]
[JsonDerivedType(typeof(MqttSubscribeResult), "Subscribe")]
[JsonDerivedType(typeof(MqttUnsubscribeResult), "Unsubscribe")]
[JsonDerivedType(typeof(MqttClientFailureResult), "Error")]
public abstract record MqttClientResult : IFlowResult
{
    protected MqttClientResult(
        string kind,
        MqttClientOperation operation,
        DateTimeOffset timestamp,
        FlowError? error = null)
    {
        Kind = kind;
        Operation = operation;
        Timestamp = timestamp;
        Error = error;
    }

    [JsonIgnore]
    public string Kind { get; }

    public MqttClientOperation Operation { get; }

    public FlowError? Error { get; }

    public bool IsError => Error is not null;

    public DateTimeOffset Timestamp { get; }
}

public sealed record MqttConnectResult : MqttClientResult
{
    public MqttConnectResult(DateTimeOffset timestamp, bool changed)
        : base("Connect", MqttClientOperation.Connect, timestamp)
    {
        Changed = changed;
    }

    public bool IsConnected => true;

    public bool Changed { get; }
}

public sealed record MqttDisconnectResult : MqttClientResult
{
    public MqttDisconnectResult(DateTimeOffset timestamp, bool changed)
        : base("Disconnect", MqttClientOperation.Disconnect, timestamp)
    {
        Changed = changed;
    }

    public bool IsConnected => false;

    public bool Changed { get; }
}

public sealed record MqttStatusResult : MqttClientResult
{
    public MqttStatusResult(DateTimeOffset timestamp, MqttClientStatus status)
        : base("Status", MqttClientOperation.Status, timestamp)
    {
        Status = status ?? throw new ArgumentNullException(nameof(status));
    }

    public MqttClientStatus Status { get; }
}

public sealed record MqttPublishOperationResult : MqttClientResult
{
    public MqttPublishOperationResult(DateTimeOffset timestamp, MqttPublishMessage message)
        : base("Publish", MqttClientOperation.Publish, timestamp)
    {
        ArgumentNullException.ThrowIfNull(message);
        Topic = message.Topic;
        Qos = message.Qos;
        Retain = message.Retain;
    }

    public string Topic { get; }

    public MqttQos Qos { get; }

    public bool Retain { get; }
}

public sealed record MqttSubscribeResult : MqttClientResult
{
    public MqttSubscribeResult(
        DateTimeOffset timestamp,
        string name,
        MqttSubscriptionDefinition subscription,
        bool changed)
        : base("Subscribe", MqttClientOperation.Subscribe, timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
        Changed = changed;
    }

    public string Name { get; }

    public MqttSubscriptionDefinition Subscription { get; }

    public bool Changed { get; }
}

public sealed record MqttUnsubscribeResult : MqttClientResult
{
    public MqttUnsubscribeResult(DateTimeOffset timestamp, string name, bool changed)
        : base("Unsubscribe", MqttClientOperation.Unsubscribe, timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Changed = changed;
    }

    public string Name { get; }

    public bool Changed { get; }
}

public sealed record MqttClientFailureResult : MqttClientResult
{
    public MqttClientFailureResult(
        MqttClientOperation operation,
        FlowError error,
        DateTimeOffset timestamp)
        : base("Error", operation, timestamp, error ?? throw new ArgumentNullException(nameof(error)))
    {
    }
}
