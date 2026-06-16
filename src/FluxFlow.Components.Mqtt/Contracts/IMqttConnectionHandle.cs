using FluxFlow.Components.Mqtt.Options;

namespace FluxFlow.Components.Mqtt.Contracts;

public interface IMqttConnectionHandle
{
    string ConnectionName { get; }
    MqttConnectionProfile Profile { get; }
    MqttReconnectPolicy? Reconnect { get; }
}
