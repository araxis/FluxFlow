namespace FluxFlow.Components.Mqtt.Configuration;

public sealed record MqttCredentialConfiguration
{
    public string? Username { get; init; }

    public string? Password { get; init; }
}
