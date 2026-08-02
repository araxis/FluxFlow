using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Subscriptions;
using System.Text.Json.Serialization;

namespace FluxFlow.Components.Mqtt.Client;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "Operation")]
[JsonDerivedType(typeof(MqttConnectRequest), "Connect")]
[JsonDerivedType(typeof(MqttDisconnectRequest), "Disconnect")]
[JsonDerivedType(typeof(MqttStatusRequest), "Status")]
[JsonDerivedType(typeof(MqttPublishClientRequest), "Publish")]
[JsonDerivedType(typeof(MqttSubscribeRequest), "Subscribe")]
[JsonDerivedType(typeof(MqttUnsubscribeRequest), "Unsubscribe")]
public abstract record MqttClientRequest
{
    protected MqttClientRequest(MqttClientOperation operation)
    {
        Operation = operation;
    }

    [JsonIgnore]
    public MqttClientOperation Operation { get; }
}

public sealed record MqttConnectRequest : MqttClientRequest
{
    public MqttConnectRequest()
        : base(MqttClientOperation.Connect)
    {
    }
}

public sealed record MqttDisconnectRequest : MqttClientRequest
{
    public MqttDisconnectRequest()
        : base(MqttClientOperation.Disconnect)
    {
    }

    public string? Reason { get; init; }
}

public sealed record MqttStatusRequest : MqttClientRequest
{
    public MqttStatusRequest()
        : base(MqttClientOperation.Status)
    {
    }
}

public sealed record MqttPublishClientRequest : MqttClientRequest
{
    public MqttPublishClientRequest()
        : base(MqttClientOperation.Publish)
    {
    }

    public required MqttPublishMessage Message { get; init; }
}

public sealed record MqttSubscribeRequest : MqttClientRequest
{
    public MqttSubscribeRequest()
        : base(MqttClientOperation.Subscribe)
    {
    }

    public required string Name { get; init; }

    public required MqttSubscriptionDefinition Subscription { get; init; }
}

public sealed record MqttUnsubscribeRequest : MqttClientRequest
{
    public MqttUnsubscribeRequest()
        : base(MqttClientOperation.Unsubscribe)
    {
    }

    public required string Name { get; init; }
}
