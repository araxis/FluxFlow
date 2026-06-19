using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Nodes;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.Tests;

/// <summary>
/// Standalone helpers for constructing the MQTT nodes by hand (no engine) and draining
/// their broadcast ports into BufferBlock sinks.
/// </summary>
internal static class MqttTestContext
{
    public const string ConnectionName = "main-broker";

    public static MqttConnectionNode CreateConnection(
        IMqttClientFactory factory,
        TimeProvider? clock = null,
        string connectionName = ConnectionName,
        MqttConnectionProfile? profile = null,
        MqttReconnectPolicy? reconnect = null)
        => new(
            connectionName,
            profile ?? new MqttConnectionProfile { Name = connectionName, Host = "localhost", Port = 1883 },
            reconnect,
            factory,
            clock);

    /// <summary>Links a broadcast source port to a BufferBlock sink that the test drains.</summary>
    public static BufferBlock<T> Sink<T>(ISourceBlock<T> source, bool propagateCompletion = false)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = propagateCompletion });
        return sink;
    }
}
