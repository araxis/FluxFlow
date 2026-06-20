namespace FluxFlow.Components.Mqtt.Options;

public sealed record MqttPublishOptions
{
    public int PublishTimeoutMilliseconds { get; init; } = 30_000;
    public int BoundedCapacity { get; init; } = 128;
}
