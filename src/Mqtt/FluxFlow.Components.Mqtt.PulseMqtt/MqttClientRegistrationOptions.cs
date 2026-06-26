namespace FluxFlow.Components.Mqtt.PulseMqtt;

public sealed record MqttClientRegistrationOptions
{
    public bool StartWithHost { get; init; }

    public bool WaitForConnectedOnStart { get; init; }
}
