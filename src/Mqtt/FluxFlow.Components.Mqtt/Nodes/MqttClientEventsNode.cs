using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttClientEventsNode : IFlowSource
{
    private readonly IMqttClientController _controller;
    private readonly int _capacity;
    private readonly MqttSourcePump<MqttClientEvent> _pump;
    private IMqttClientEventSubscription? _subscription;

    public MqttClientEventsNode(
        IMqttClientController controller,
        int maximumPendingEvents = 128)
    {
        if (maximumPendingEvents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingEvents));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _capacity = maximumPendingEvents;
        _pump = new MqttSourcePump<MqttClientEvent>(
            maximumPendingEvents,
            RunCoreAsync,
            DisposeCoreAsync);
    }

    public ISourceBlock<FlowMessage<MqttClientEvent>> Output => _pump.Output;

    public ISourceBlock<FlowEvent> Events => _pump.Events;

    public Task Completion => _pump.Completion;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _pump.StartAsync(cancellationToken);

    public void Complete() => _pump.Complete();

    public void Fault(Exception exception) => _pump.Fault(exception);

    public ValueTask DisposeAsync() => _pump.DisposeAsync();

    private async Task RunCoreAsync(CancellationToken cancellationToken)
    {
        _subscription = await _controller.SubscribeEventsAsync(_capacity, cancellationToken)
            .ConfigureAwait(false);
        await foreach (var @event in _subscription.Events
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            await _pump.EmitAsync(FlowMessage.Create(@event), cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask DisposeCoreAsync()
    {
        if (_subscription is not null)
            await _subscription.DisposeAsync().ConfigureAwait(false);
    }
}
