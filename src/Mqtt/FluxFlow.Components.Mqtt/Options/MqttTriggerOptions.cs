using FluxFlow.Components.Mqtt.Contracts;

namespace FluxFlow.Components.Mqtt.Options;

public sealed record MqttTriggerOptions
{
    public string? TopicFilter { get; init; }

    public MqttQualityOfService QualityOfService { get; init; } = MqttQualityOfService.AtMostOnce;

    public bool ReceiveRetainedMessages { get; init; } = true;

    public bool RetainAsPublished { get; init; }

    public int BoundedCapacity { get; init; } = 128;

    public MqttTriggerMode Mode { get; init; } = MqttTriggerMode.FireAndForget;

    public MqttTriggerAcknowledgement Acknowledgement { get; init; } = MqttTriggerAcknowledgement.None;

    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

public enum MqttTriggerMode
{
    FireAndForget = 0,
    RequestReply = 1
}

public enum MqttTriggerAcknowledgement
{
    None = 0,
    OnEmit = 1,
    OnSuccessfulResponse = 2
}
