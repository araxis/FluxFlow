namespace FluxFlow.Components.Mqtt.Composition;

public sealed record MqttPublishCompositionOptions
{
    public int MaximumPendingRequests { get; init; } = 128;
}
