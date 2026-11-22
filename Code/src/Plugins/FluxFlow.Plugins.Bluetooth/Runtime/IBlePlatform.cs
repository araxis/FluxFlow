namespace FluxFlow.Components.Bluetooth;

public interface IBlePlatform
{
    Task<byte[]> ReadAsync(BleClientOptions options, CancellationToken cancellation);
    Task WriteAsync(BleClientOptions options, byte[] value, BleWriteMode mode, CancellationToken cancellation)
        => Task.FromException(new PlatformNotSupportedException("This BLE provider does not support writes."));
    Task SubscribeAsync(BleClientOptions options, Func<byte[], Task> received, Action started, CancellationToken cancellation)
        => Task.FromException(new PlatformNotSupportedException("This BLE provider does not support subscriptions."));
    Task ServeAsync(BleServerOptions options, Action started, CancellationToken cancellation);
    Task ServeAsync(BleServerOptions options, BleServerSession session, Action started, CancellationToken cancellation)
        => Task.FromException(new PlatformNotSupportedException("This BLE provider does not support workflow-backed GATT server sessions."));
}

public sealed class UnavailableBlePlatform : IBlePlatform
{
    private static Exception Missing() => new PlatformNotSupportedException("BLE requires the Windows BLE companion. The browser does not access Bluetooth directly.");
    public Task<byte[]> ReadAsync(BleClientOptions options, CancellationToken cancellation) => Task.FromException<byte[]>(Missing());
    public Task ServeAsync(BleServerOptions options, Action started, CancellationToken cancellation) => Task.FromException(Missing());
    public Task ServeAsync(BleServerOptions options, BleServerSession session, Action started, CancellationToken cancellation) => Task.FromException(Missing());
}
