using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace FluxFlow.Components.Bluetooth.Windows;

internal readonly record struct GattStatus(GattServiceProviderAdvertisementStatus Status, BluetoothError Error)
{
    public bool IsAdvertising => Status is GattServiceProviderAdvertisementStatus.Started
        or GattServiceProviderAdvertisementStatus.StartedWithoutAllAdvertisementData;
}

// One instance per native provider invocation; queued callbacks cannot affect later sessions.
internal sealed class GattSessionSignals(ILogger logger) : IDisposable
{
    private readonly object _gate = new();
    private bool _closed;
    private bool _stopping;
    public TaskCompletionSource<GattStatus> Startup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<GattStatus> Interrupted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Observe(GattServiceProviderAdvertisementStatus status, BluetoothError error)
    {
        lock (_gate)
        {
            if (_closed) return;
            logger.LogInformation("GATT advertising status: {Status}; Windows error: {Error}", status, error);
            var value = new GattStatus(status, error);
            // Windows can emit Aborted/Success before Started. It does not settle startup.
            if (value.IsAdvertising || (status == GattServiceProviderAdvertisementStatus.Aborted && error != BluetoothError.Success))
                Startup.TrySetResult(value);
            if (status is GattServiceProviderAdvertisementStatus.Stopped or GattServiceProviderAdvertisementStatus.Aborted)
            {
                if (Startup.Task.IsCompletedSuccessfully && Startup.Task.Result.IsAdvertising) Interrupted.TrySetResult(value);
                if (_stopping && status == GattServiceProviderAdvertisementStatus.Stopped) Stopped.TrySetResult();
            }
        }
    }

    public void BeginStop() { lock (_gate) _stopping = true; }
    public void Dispose() { lock (_gate) _closed = true; }
}
