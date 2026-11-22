using System.Text.Json.Serialization;

namespace FluxFlow.Components.Bluetooth;

public interface IBleExplorer
{
    Task<BleDeviceInfo[]> ScanAsync(CancellationToken cancellation);
    Task<BleServiceInfo[]> DiscoverAsync(BleClientOptions device, CancellationToken cancellation);
    Task<byte[]> ReadAsync(BleClientOptions options, CancellationToken cancellation);
}

public sealed record BleDeviceInfo(string Address, BleAddressType AddressType, string Name, short SignalStrength);
public sealed record BleServiceInfo(Guid Id, BleCharacteristicInfo[] Characteristics);
public sealed record BleCharacteristicInfo(Guid Id, bool Readable, string Properties);
public sealed record BleExploreResult
{
    public BleDeviceInfo[] Devices { get; init; } = [];
    public BleServiceInfo[] Services { get; init; } = [];
    public byte[]? Value { get; init; }
    public string? Error { get; init; }
}

public sealed class UnavailableBleExplorer : IBleExplorer
{
    private static Exception Missing() => new PlatformNotSupportedException("BLE discovery requires a Windows BLE companion. Manual configuration is still available.");
    public Task<BleDeviceInfo[]> ScanAsync(CancellationToken cancellation) => Task.FromException<BleDeviceInfo[]>(Missing());
    public Task<BleServiceInfo[]> DiscoverAsync(BleClientOptions device, CancellationToken cancellation) => Task.FromException<BleServiceInfo[]>(Missing());
    public Task<byte[]> ReadAsync(BleClientOptions options, CancellationToken cancellation) => Task.FromException<byte[]>(Missing());
}

[JsonSerializable(typeof(BleClientOptions))]
[JsonSerializable(typeof(BleExploreResult))]
public partial class BleExplorerJsonContext : JsonSerializerContext;
