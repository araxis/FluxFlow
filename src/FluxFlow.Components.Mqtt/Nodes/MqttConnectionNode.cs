using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Engine.Components;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttConnectionNode : FlowNodeBase, IMqttConnectionHandle
{
    public MqttConnectionNode(
        string connectionName,
        MqttConnectionProfile profile,
        MqttReconnectPolicy? reconnect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        ArgumentNullException.ThrowIfNull(profile);

        ConnectionName = connectionName;
        Profile = profile;
        Reconnect = reconnect;
    }

    public string ConnectionName { get; }

    public MqttConnectionProfile Profile { get; }

    public MqttReconnectPolicy? Reconnect { get; }
}
