namespace FluxFlow.Components.Bluetooth;

/// <summary>Saved device identity, resolved per run. It does not open or own a native handle.</summary>
public sealed record BleConnection(string Endpoint)
{
    public BleClientOptions Apply(BleClientOptions options)
    {
        var uri = Parse("ble");
        var type = uri.AbsolutePath.Trim('/');
        if (type is not ("public" or "random")) throw new ArgumentException("A BLE peripheral endpoint uses ble://AABBCCDDEEFF/public or /random.");
        var result = options with { DeviceAddress = uri.Host, AddressType = type == "public" ? BleAddressType.Public : BleAddressType.Random };
        result.ValidateDevice();
        return result;
    }
    public void ValidateAdapter()
    {
        var uri = Parse("ble-adapter");
        if (uri.Host != "default" || uri.AbsolutePath is not ("" or "/"))
            throw new ArgumentException("This companion supports only ble-adapter://default on its execution host. Selecting another adapter is not supported.");
    }
    private Uri Parse(string scheme)
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != scheme || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.Port != -1)
            throw new ArgumentException($"Invalid {scheme} endpoint. Credentials, ports, queries and fragments are not supported.");
        return uri;
    }
}

public interface IBleCapabilities
{
    /// <summary>Checks the execution host without enabling a radio, scanning or connecting.</summary>
    Task CheckAsync(bool peripheral, CancellationToken cancellation);
}
