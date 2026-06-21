namespace FluxFlow.Components.Mqtt.PulseMqtt;

public sealed record MqttClientRegistrationOptions
{
    public bool StartWithHost { get; init; } = true;

    public bool WaitForConnectedOnStart { get; init; }
}
