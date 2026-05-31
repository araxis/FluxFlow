using FluxFlow.Components.Mqtt.Contracts;

namespace FluxFlow.Components.Mqtt.Options;

public sealed record MqttSubscriptionOptions
{
    public MqttConnectionProfile Connection { get; init; } = new();
    public string? TopicFilter { get; init; }
    public MqttQualityOfService QualityOfService { get; init; } = MqttQualityOfService.AtMostOnce;
    public int BoundedCapacity { get; init; } = 128;
}
