using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttClientEventsNode : FlowSource
{
    private readonly IMqttClientController _controller;
    private readonly int _capacity;
    private IMqttClientEventSubscription? _subscription;

    public MqttClientEventsNode(
        IMqttClientController controller,
        int maximumPendingEvents = 128)
        : base(CreateSourceOptions(controller, maximumPendingEvents))
    {
        _controller = controller;
        _capacity = maximumPendingEvents;
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        _subscription = await _controller.SubscribeEventsAsync(_capacity, cancellationToken)
            .ConfigureAwait(false);
        await foreach (var @event in _subscription.Events
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            await EmitAsync(
                FlowMessage.Create(FlowValue.From<MqttClientEvent>(@event)),
                cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async ValueTask OnDisposeAsync()
    {
        if (_subscription is not null)
            await _subscription.DisposeAsync().ConfigureAwait(false);
    }

    private static FlowSourceOptions CreateSourceOptions(IMqttClientController controller, int maximumPendingEvents)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (maximumPendingEvents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingEvents));
        return new FlowSourceOptions { OutputCapacity = maximumPendingEvents };
    }
}
