namespace FluxFlow.Components.Mqtt.Configuration;

public sealed record MqttReconnectConfiguration
{
    public bool Enabled { get; init; } = true;

    public MqttRetryPolicy Policy { get; init; } = new();
}
