namespace FluxFlow.Components.Mqtt.Configuration;

public sealed record MqttBrokerConfiguration
{
    public required string Host { get; init; }

    public int Port { get; init; } = 1883;

    public bool UseTls { get; init; }

    public string? ServerName { get; init; }
}
