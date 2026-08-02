using System.Threading.Channels;

namespace FluxFlow.Components.Mqtt.Events;

public interface IMqttClientEventSubscription : IAsyncDisposable
{
    IAsyncEnumerable<MqttClientEvent> Events { get; }
}

internal sealed class MqttClientEventSubscription : IMqttClientEventSubscription
{
    private readonly Channel<MqttClientEvent> _events;
    private readonly Action<MqttClientEventSubscription> _dispose;
    private int _disposed;

    public MqttClientEventSubscription(
        int capacity,
        Action<MqttClientEventSubscription> dispose)
    {
        _dispose = dispose;
        _events = Channel.CreateBounded<MqttClientEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public IAsyncEnumerable<MqttClientEvent> Events => _events.Reader.ReadAllAsync();

    internal ValueTask WriteAsync(MqttClientEvent @event, CancellationToken cancellationToken)
        => _events.Writer.WriteAsync(@event, cancellationToken);

    internal bool TryWrite(MqttClientEvent @event) => _events.Writer.TryWrite(@event);

    internal void Complete() => _events.Writer.TryComplete();

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Complete();
            _dispose(this);
        }

        return ValueTask.CompletedTask;
    }
}
