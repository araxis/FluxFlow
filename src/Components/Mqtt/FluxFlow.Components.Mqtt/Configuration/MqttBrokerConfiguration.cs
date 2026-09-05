namespace FluxFlow.Components.Mqtt.Configuration;

public sealed record MqttBrokerConfiguration
{
    public required string Host { get; init; }

    public int Port { get; init; } = 1883;

    public MqttBrokerTransport Transport { get; init; } = MqttBrokerTransport.Tcp;

    public bool UseTls { get; init; }

    public string? ServerName { get; init; }

    public string WebSocketPath { get; init; } = "/mqtt";
}
