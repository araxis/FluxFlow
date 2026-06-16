using FluxFlow.Components.Mqtt.Contracts;

namespace FluxFlow.Components.Mqtt.Options;

public sealed record MqttConnectionOptions
{
    public MqttConnectionProfile Profile { get; init; } = new();
    public MqttReconnectPolicy? Reconnect { get; init; }
}
