using FluxFlow.Components.Bluetooth;

namespace FluxFlow.Plugins.Bluetooth.Design;

public static class BleDiscoveryAddress
{
    public static BleClientOptions Resolve(IReadOnlyList<string> endpoints)
    {
        var targets = endpoints.Select(endpoint => new BleConnection(endpoint).Apply(new())).ToArray();
        if (targets.Length == 0) throw new ArgumentException("The peripheral connection is unavailable.");
        if (targets.Select(target => (target.Address, target.AddressType)).Distinct().Skip(1).Any())
            throw new ArgumentException("Application instances bind this connection to different devices. Choose a direct device for discovery; the saved connection remains unchanged until OK.");
        return targets[0];
    }
}
