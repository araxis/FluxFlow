using FluxFlow.Components.Mqtt.Events;
using System.Threading.Channels;

namespace FluxFlow.Components.Mqtt.Client;

internal sealed class MqttClientEventHub
{
    private readonly object _gate = new();
    private readonly List<MqttClientEventSubscription> _subscriptions = [];
    private MqttClientEvent? _lastConnectionEvent;

    internal ValueTask<IMqttClientEventSubscription> SubscribeAsync(
        int capacity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        var subscription = new MqttClientEventSubscription(capacity, Remove);
        lock (_gate)
        {
            _subscriptions.Add(subscription);
            if (_lastConnectionEvent is not null)
                subscription.TryWrite(_lastConnectionEvent);
        }
        return ValueTask.FromResult<IMqttClientEventSubscription>(subscription);
    }

    internal async ValueTask PublishAsync(
        MqttClientEvent @event,
        CancellationToken cancellationToken)
    {
        MqttClientEventSubscription[] subscriptions;
        lock (_gate)
        {
            if (@event is MqttClientConnectedEvent or MqttClientDisconnectedEvent)
            {
                if ((_lastConnectionEvent is MqttClientConnectedEvent &&
                        @event is MqttClientConnectedEvent) ||
                    (_lastConnectionEvent is MqttClientDisconnectedEvent &&
                        @event is MqttClientDisconnectedEvent))
                {
                    return;
                }

                _lastConnectionEvent = @event;
            }

            subscriptions = _subscriptions.ToArray();
        }

        foreach (var subscription in subscriptions)
        {
            try
            {
                await subscription.WriteAsync(@event, cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                // A subscriber can close after the publication snapshot was captured.
            }
        }
    }

    internal MqttClientEventSubscription[] DetachAll()
    {
        lock (_gate)
        {
            var subscriptions = _subscriptions.ToArray();
            _subscriptions.Clear();
            return subscriptions;
        }
    }

    private void Remove(MqttClientEventSubscription subscription)
    {
        lock (_gate)
            _subscriptions.Remove(subscription);
    }
}
