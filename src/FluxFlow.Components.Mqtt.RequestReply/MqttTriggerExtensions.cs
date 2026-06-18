using FluxFlow.Components.RequestReply;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.RequestReply;

public static class MqttTriggerExtensions
{
    /// <summary>
    /// Feeds one inbound MQTT request into the bridge. The host calls this from its MQTT
    /// subscription handler; the reply is published through <paramref name="publisher"/>
    /// when the graph answers. Returns false if the bridge is not accepting (shutting down).
    /// </summary>
    public static Task<bool> SubmitAsync(
        this RequestReplyBridge<MqttRequest, MqttReply> bridge,
        MqttRequest request,
        IMqttResponsePublisher publisher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        return bridge.Incoming.SendAsync(new MqttRequestContext(request, publisher), cancellationToken);
    }
}
