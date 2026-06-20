using FluxFlow.Components.Mqtt.Contracts;
using Pulse.Mqtt.Packets;
using Pulse.Mqtt.Resilience;

namespace FluxFlow.Components.Mqtt.PulseMqtt;

internal sealed class RejectingMessageStore : IMessageStore
{
    private long _dropped;

    public int Count => 0;

    public long DroppedCount => Interlocked.Read(ref _dropped);

    public ValueTask EnqueueAsync(
        MqttPublishPacket packet,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _dropped);
        return new ValueTask(Task.FromException(new MqttClientUnavailableException(
            "Pulse MQTT client is not connected.")));
    }

    public ValueTask<MqttPublishPacket?> PeekAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult<MqttPublishPacket?>(null);

    public ValueTask RemoveHeadAsync(CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    public ValueTask ClearAsync(CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}
