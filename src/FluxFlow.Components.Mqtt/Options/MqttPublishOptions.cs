using FluxFlow.Components.Mqtt.Contracts;

namespace FluxFlow.Components.Mqtt.Options;

public sealed record MqttPublishOptions
{
    public string? ConnectionName { get; init; }
    public MqttConnectionProfile Connection { get; init; } = new();
    public string? DefaultTopic { get; init; }
    public MqttQualityOfService QualityOfService { get; init; } = MqttQualityOfService.AtMostOnce;
    public bool Retain { get; init; }
    public MqttReconnectPolicy? Reconnect { get; init; }
    public int PublishTimeoutMilliseconds { get; init; } = 30_000;
    public int BoundedCapacity { get; init; } = 128;
}
