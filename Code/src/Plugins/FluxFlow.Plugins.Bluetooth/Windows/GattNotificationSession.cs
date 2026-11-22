using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace FluxFlow.Components.Bluetooth.Windows;

internal sealed class GattNotificationSession
{
    private readonly Channel<byte[]> _queue;
    private readonly object _gate = new();
    private readonly CancellationToken _stop;
    private bool _closed;
    private ExceptionDispatchInfo? _failure;
    public GattClientCharacteristicConfigurationDescriptorValue Descriptor { get; }

    public GattNotificationSession(BleClientOptions options, GattCharacteristicProperties properties, CancellationToken stop)
    {
        var indicate = options.SubscriptionMode == BleSubscriptionMode.Indicate;
        if (!properties.HasFlag(indicate ? GattCharacteristicProperties.Indicate : GattCharacteristicProperties.Notify))
            throw new InvalidOperationException($"Selected characteristic does not support {options.SubscriptionMode}.");
        Descriptor = indicate ? GattClientCharacteristicConfigurationDescriptorValue.Indicate : GattClientCharacteristicConfigurationDescriptorValue.Notify;
        _queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(options.OutputCapacity) { SingleReader = true });
        _stop = stop;
    }

    public void Receive(Func<byte[]> copy)
    {
        lock (_gate)
        {
            if (_closed || _stop.IsCancellationRequested) return;
            try
            {
                if (!_queue.Writer.TryWrite(copy()))
                    Fail(new InvalidOperationException("BLE notification buffer is full. Increase capacity or reduce the device's notification rate."));
            }
            catch (Exception error) { Fail(error); }
        }
    }

    public void ObserveConnection(Func<bool> disconnected)
    {
        lock (_gate)
        {
            if (_closed || _stop.IsCancellationRequested) return;
            if (disconnected()) Fail(new InvalidOperationException("BLE device disconnected during the subscription."));
        }
    }

    // Called under the callback lock. Closing the queue wakes the reader; storing
    // the cause separately keeps a later cancellation from replacing it.
    private void Fail(Exception error)
    {
        _closed = true;
        _failure = ExceptionDispatchInfo.Capture(error);
        _queue.Writer.TryComplete();
    }

    public async Task DeliverAsync(Action started, Func<byte[], Task> received)
    {
        lock (_gate)
        {
            _failure?.Throw();
            _stop.ThrowIfCancellationRequested();
            started();
        }
        try
        {
            await foreach (var bytes in _queue.Reader.ReadAllAsync(_stop).ConfigureAwait(false))
            {
                _stop.ThrowIfCancellationRequested();
                await received(bytes).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        lock (_gate) _failure?.Throw();
        _stop.ThrowIfCancellationRequested();
    }

    public void Close()
    {
        lock (_gate) { _closed = true; _queue.Writer.TryComplete(); }
    }
}
