using FluxFlow.Components.Bluetooth;

namespace FluxFlow.Plugins.Bluetooth.Design;

public sealed class BleClientDiscoverySession(IBleExplorer explorer) : IDisposable
{
    private CancellationTokenSource? _pending;
    private bool _disposed;
    public bool Busy => _pending is not null;
    public string? Status { get; private set; }
    public string? Error { get; private set; }
    public BleDeviceInfo[] Devices { get; private set; } = [];
    public BleServiceInfo[] Services { get; private set; } = [];
    public byte[]? Value { get; private set; }
    public Task ScanAsync(Func<bool>? isCurrent = null)
    {
        if (_disposed || Busy) return Task.CompletedTask;
        Devices = [];
        return Run("Scanning for 5 seconds…", "Scan complete. Select a device or enter its address.", async token => { var items = await explorer.ScanAsync(token); return () => Devices = items; }, isCurrent);
    }
    public Task DiscoverAsync(BleClientOptions options, Func<bool>? isCurrent = null)
    {
        if (_disposed || Busy) return Task.CompletedTask;
        Services = []; Value = null;
        return Run("Discovering services…", "Discovery complete. Connection released.", async token => { var items = await explorer.DiscoverAsync(options, token); return () => Services = items; }, isCurrent);
    }
    public Task ReadAsync(BleClientOptions options, Func<bool>? isCurrent = null)
    {
        if (_disposed || Busy) return Task.CompletedTask;
        Value = null;
        return Run("Reading characteristic…", "Read complete. Connection released.", async token => { var bytes = await explorer.ReadAsync(options, token); return () => Value = bytes.ToArray(); }, isCurrent);
    }
    public void ClearTarget() { Cancel(); Services = []; Value = null; Status = null; Error = null; }
    public void ClearValue() { Cancel(); Value = null; Status = null; Error = null; }
    public void Cancel()
    {
        if (_pending is not { } pending) return;
        _pending = null;
        pending.Cancel();
        Status = "Canceled. The companion releases any pending native operation.";
    }
    private async Task Run(string started, string completed, Func<CancellationToken, Task<Action>> action, Func<bool>? isCurrent)
    {
        if (_disposed || Busy) return;
        using var pending = new CancellationTokenSource();
        _pending = pending;
        Status = started; Error = null;
        try
        {
            var apply = await action(pending.Token);
            if (_disposed || pending.IsCancellationRequested || !ReferenceEquals(_pending, pending)) return;
            if (isCurrent?.Invoke() == false) { Status = "Results discarded because the project changed."; return; }
            apply(); Status = completed;
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!_disposed && ReferenceEquals(_pending, pending))
            {
                if (isCurrent?.Invoke() == false) { Status = "Results discarded because the project changed."; return; }
                Error = error.Message; Status = "Operation failed.";
            }
        }
        finally { if (ReferenceEquals(_pending, pending)) _pending = null; }
    }
    public void Dispose() { _disposed = true; Cancel(); }
}
