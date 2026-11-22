using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Components.Bluetooth;
using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.Bluetooth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Plugins.Bluetooth.Design;

public sealed class BluetoothDesign : IPluginDesign
{
    public void ConfigureRemoteServices(IServiceCollection services)
    {
        BleInfrastructure.Register(services);
        services.AddScoped<IBleExplorer, HttpBleExplorer>();
    }

    public static PluginConnection Peripheral { get; } = Connection(BleInfrastructure.Services.Single(item => item.Type == "ble.peripheral"));
    public static PluginConnection Adapter { get; } = Connection(BleInfrastructure.Services.Single(item => item.Type == "ble.adapter"));
    private static PluginConnection Connection(FluxFlow.Environments.InfrastructureServiceType service) => new(service.Type, service.Name, "Endpoint", false,
        JsonSerializer.SerializeToElement(new { service.Type, Endpoint = service.DefaultEndpoint }),
        [OptionEditorModel.Create("Endpoint", "Endpoint", defaultValue: service.DefaultEndpoint) with { IsRequired = true }]);

    public PropertyRules Connection(PluginConnection connection) => BleConnectionFields.Describe(connection.Type == Adapter.Type);
    public PropertyRules Component(PluginComponent component) => new()
    {
        Interactive = component.Palette.ComponentType == "ble.server" ? null : provider => new BleTargetInspector(provider.GetRequiredService<IBleExplorer>(), LocalInspector(component.Palette.ComponentType)),
        Hierarchy = component.Palette.ComponentType == "ble.server" ? BleGattDocument.Editor : null,
        Inspector = component.Palette.ComponentType == "ble.server" ? _ => component.Inspector with { Sections = FluxFlow.Authoring.Definitions.BleServer.BleServerDescription.SettingsSections } : null,
        Validate = section => ValidateComponent(component.Palette.ComponentType, (IConfigurationSection)section),
        ValidationNote = _ => new("BLE hardware access requires the Windows BLE companion. GATT server Stop confirmation is experimental.", true),
        TextValidators = LocalInspector(component.Palette.ComponentType).Sections.SelectMany(s => s.Options).Where(o => o.ValidateText is not null)
            .ToDictionary(o => o.Name, o => o.ValidateText!)
    };

    private static NodeInspectorModel LocalInspector(string type) => type switch
    {
        "ble.server" => new FluxFlow.Authoring.Definitions.BleServer.BleServerDescription().Inspector,
        "ble.notifications" => new FluxFlow.Authoring.Definitions.BleNotifications.BleNotificationsDescription().Inspector,
        _ => new FluxFlow.Authoring.Definitions.BleClient.BleClientDescription().Inspector
    };

    internal static void ValidateComponent(string type, IConfigurationSection section)
    {
        if (type == "ble.server")
        {
            var server = section.ReadRequiredConfiguration<BleServerConfiguration>();
            server.ToOptions().Validate();
            if (server.Adapter is not null) ArgumentException.ThrowIfNullOrWhiteSpace(server.Adapter);
            return;
        }
        var subscription = type == "ble.notifications";
        if (subscription && section.GetSection("Input").Exists()) throw new ArgumentException("BLE Notifications is a source and has no Input port.");
        section.ReadRequiredConfiguration<BleClientConfiguration>().Validate(subscription);
    }
}
