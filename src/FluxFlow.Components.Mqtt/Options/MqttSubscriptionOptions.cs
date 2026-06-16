using FluxFlow.Components.Mqtt.Contracts;

namespace FluxFlow.Components.Mqtt.Options;

public sealed record MqttSubscriptionOptions
{
    public string? ConnectionName { get; init; }
    public string? TopicFilter { get; init; }
    public MqttQualityOfService QualityOfService { get; init; } = MqttQualityOfService.AtMostOnce;
    public bool ReceiveRetainedMessages { get; init; } = true;
    public bool RetainAsPublished { get; init; }
    public int BoundedCapacity { get; init; } = 128;
}
