using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Radios;

namespace FluxFlow.Components.Bluetooth.Windows;

public sealed class WindowsBlePlatform(ILoggerFactory logs) : IBlePlatform, IBleExplorer, IBleCapabilities, IDisposable
{
    private readonly WindowsBle _server = new(logs.CreateLogger<WindowsBle>());
    private readonly SemaphoreSlim _serverGate = new(1, 1);
    private readonly ConcurrentDictionary<(ulong, BleAddressType, Guid, Guid), byte> _subscriptions = new();
    public Task CheckAsync(bool peripheral, CancellationToken cancellation) => RequireRadioAsync(peripheral, cancellation);
    public async Task<BleDeviceInfo[]> ScanAsync(CancellationToken cancellation)
    {
        await RequireRadioAsync(false, cancellation).ConfigureAwait(false);
        using var duration = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        duration.CancelAfter(TimeSpan.FromSeconds(5));
        var devices = new Dictionary<(string, BleAddressType), BleDeviceInfo>();
        var scanner = new WindowsBle(logs.CreateLogger<WindowsBle>());
        try
        {
            await scanner.ScanAsync(duration.Token, item =>
            {
                var key = (item.Address, item.AddressType);
                if (devices.TryGetValue(key, out var previous) && string.IsNullOrEmpty(item.Name)) item = item with { Name = previous.Name };
                if (devices.ContainsKey(key) || devices.Count < 256) devices[key] = item;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (duration.IsCancellationRequested && !cancellation.IsCancellationRequested) { }
        cancellation.ThrowIfCancellationRequested();
        return devices.Values.OrderByDescending(item => item.SignalStrength).ToArray();
    }

    public async Task<BleServiceInfo[]> DiscoverAsync(BleClientOptions device, CancellationToken cancellation)
    {
        device.ValidateDevice();
        await RequireRadioAsync(false, cancellation).ConfigureAwait(false);
        return await WindowsBle.DiscoverAsync(device, cancellation).ConfigureAwait(false);
    }
    public async Task<byte[]> ReadAsync(BleClientOptions options, CancellationToken cancellation)
    {
        options.Validate();
        await RequireRadioAsync(false, cancellation).ConfigureAwait(false);
        return await WindowsBle.ReadAsync(options.Address,
            options.AddressType == BleAddressType.Public ? BluetoothAddressType.Public : BluetoothAddressType.Random,
            options.ServiceId, options.CharacteristicId, cancellation).ConfigureAwait(false);
    }

    public async Task ServeAsync(BleServerOptions options, Action started, CancellationToken cancellation)
        => await ServeAsync(options, new BleServerSession(options, _ => Task.CompletedTask, cancellation), started, cancellation).ConfigureAwait(false);

    public async Task ServeAsync(BleServerOptions options, BleServerSession session, Action started, CancellationToken cancellation)
    {
        options.Validate();
        await RequireRadioAsync(true, cancellation).ConfigureAwait(false);
        if (!await _serverGate.WaitAsync(0, cancellation).ConfigureAwait(false))
            throw new InvalidOperationException("This Windows companion supports one active BLE GATT server. Define multiple services within that server.");
        try { await _server.ServeAsync(cancellation, started: started, options: options, session: session).ConfigureAwait(false); }
        finally { _serverGate.Release(); }
    }

    public async Task WriteAsync(BleClientOptions options, byte[] value, BleWriteMode mode, CancellationToken cancellation)
    {
        options.Validate();
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Invalid BLE write mode.");
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 512) throw new ArgumentException("BLE values must contain at most 512 bytes.");
        await RequireRadioAsync(false, cancellation).ConfigureAwait(false);
        await WindowsBle.WriteAsync(options, value, mode, cancellation).ConfigureAwait(false);
    }

    public async Task SubscribeAsync(BleClientOptions options, Func<byte[], Task> received, Action started, CancellationToken cancellation)
    {
        options.ValidateSubscription();
        await RequireRadioAsync(false, cancellation).ConfigureAwait(false);
        await WithSubscriptionAsync(options, () => WindowsBle.SubscribeAsync(options, received, started, cancellation)).ConfigureAwait(false);
    }

    internal async Task WithSubscriptionAsync(BleClientOptions options, Func<Task> subscribe)
    {
        var key = (options.Address, options.AddressType, options.ServiceId, options.CharacteristicId);
        if (!_subscriptions.TryAdd(key, 0)) throw new InvalidOperationException("This characteristic already has an active subscription in this companion. Share one BLE Notifications output between consumers.");
        try { await subscribe().ConfigureAwait(false); }
        finally { _subscriptions.TryRemove(key, out _); }
    }

    private static async Task RequireRadioAsync(bool peripheral, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var adapter = await BluetoothAdapter.GetDefaultAsync();
        cancellation.ThrowIfCancellationRequested();
        if (adapter is null || !adapter.IsLowEnergySupported) throw new PlatformNotSupportedException("No BLE adapter is available on the execution host.");
        if (peripheral ? !adapter.IsPeripheralRoleSupported : !adapter.IsCentralRoleSupported)
            throw new PlatformNotSupportedException($"The Bluetooth adapter does not support the required {(peripheral ? "peripheral" : "central")} role.");
        var radio = await adapter.GetRadioAsync();
        cancellation.ThrowIfCancellationRequested();
        if (radio?.State != RadioState.On) throw new InvalidOperationException("Bluetooth is off on the execution host. Enable it in Windows before Run.");
    }
    public void Dispose() => _serverGate.Dispose();
}

public static class WindowsBleRegistration
{
    public static IServiceCollection AddWindowsBluetooth(this IServiceCollection services) => services
        .AddSingleton<WindowsBlePlatform>()
        .AddSingleton<IBlePlatform>(provider => provider.GetRequiredService<WindowsBlePlatform>())
        .AddSingleton<IBleExplorer>(provider => provider.GetRequiredService<WindowsBlePlatform>());
}
