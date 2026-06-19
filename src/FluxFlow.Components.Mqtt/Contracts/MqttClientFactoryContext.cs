using FluxFlow.Components.Mqtt.Options;

namespace FluxFlow.Components.Mqtt.Contracts;

public sealed record MqttClientFactoryContext
{
    public string? ConnectionName { get; init; }
    public required MqttConnectionProfile Profile { get; init; }
    public MqttReconnectPolicy? Reconnect { get; init; }
    public TimeProvider Clock { get; init; } = TimeProvider.System;
}
