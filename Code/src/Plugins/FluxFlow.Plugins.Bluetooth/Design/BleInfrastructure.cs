using FluxFlow.Environments;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Plugins.Bluetooth.Design;

public static class BleInfrastructure
{
    public static IReadOnlyList<InfrastructureServiceType> Services { get; } = [
        new InfrastructureServiceType("ble.peripheral", "BLE peripheral", "ble://000000000001/public", "Replace the example address with the peripheral's current 12-digit address. Use /public or /random. This describes a nearby device, not a remote Bluetooth proxy.",
            uri => uri.Scheme == "ble" && uri.Port == -1 && uri.Query.Length == 0 && uri.Host.Length == 12 && uri.Host.All(Uri.IsHexDigit) && uri.Host.Any(c => c != '0') && uri.AbsolutePath is "/public" or "/random") { Icon = FluxFlow.Authoring.Definitions.BleClient.BleClientDescription.BluetoothIcon },
        new InfrastructureServiceType("ble.adapter", "BLE adapter", "ble-adapter://default", "The default Bluetooth adapter belongs to this host. Only the execution host's default adapter is supported; selecting remote or additional adapters is not supported.",
            uri => uri.Scheme == "ble-adapter" && uri.Host == "default" && uri.Port == -1 && uri.AbsolutePath is ("" or "/") && uri.Query.Length == 0) { RequiresApplicationHost = true, Icon = FluxFlow.Authoring.Definitions.BleClient.BleClientDescription.BluetoothIcon }
    ];

    public static void Register(IServiceCollection services)
    {
        foreach (var type in Services) services.AddSingleton<IResourceEnvironmentAdapter>(new EndpointEnvironmentAdapter(type));
    }
}
