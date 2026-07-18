namespace FluxFlow.Components.Mqtt.Transport;

public sealed record MqttTransportCapabilities
{
    public bool DeferredAcknowledgement { get; init; }

    public bool NegativeAcknowledgement { get; init; }
}
