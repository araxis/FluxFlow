using System.Text.Json;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.BleServer;

public sealed class BleServerDescription : IComponentDescription
{
    public static IReadOnlyList<OptionSectionModel> SettingsSections { get; } = [
        new() { Name = "Adapter", Options = [
            Create("Adapter", "Connection", help: "Optional named ble.adapter connection owned by this execution host. Uses the host's default adapter when unset.") with { RelatedResource = "ble.adapter" }
        ] },
        new() { Name = "Advertising", Options = [
            Create("IsConnectable", "Connectable", OptionEditorKind.Toggle, "Allow BLE clients to connect to the published services.", true),
            Create("IsDiscoverable", "Discoverable", OptionEditorKind.Toggle, "Allow discovery. Windows manages the radio identity and advertising payload; availability depends on adapter capabilities.", true)
        ] },
        new() { Name = "Execution", Options = [
            Create("OutputCapacity", "Output capacity", OptionEditorKind.Integer, "Maximum number of buffered output messages.", 128) with { Min = 1, Max = int.MaxValue }
        ] }
    ];
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "ble.server", DisplayName = "BLE GATT Server", Category = "Devices", PreferredNodeName = "ble-server",
        Icon = BleClient.BleClientDescription.BluetoothIcon, DefaultColor = "#aa8de0",
        Summary = "Publishes GATT services with explicit values, access protection and workflow updates. Incoming writes are emitted on Output. Windows may not confirm advertising Stop.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output }, new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };
    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "ble.server", PrimaryEditor = "Characteristics", Sections = [new() { Name = "GATT service", Options = [
            Create("ServiceId", "Service UUID", help: "Legacy single-service UUID. Use the shared editor for multiple services.", defaultValue: "3f63b38e-98df-4a52-a9f8-76b803a934b2"),
            Create("Characteristics", "Characteristics", OptionEditorKind.Json,
                "Legacy single-service characteristics. The shared editor supports multiple services, properties, protection and binary values. Maximum 512 bytes per value.",
                JsonSerializer.Deserialize<JsonElement>("""[{"Id":"f50d7529-4e5d-4088-b88f-e236cd1e7f29","Value":"Hello from FluxFlow"}]""")) with { Syntax = "json" }
        ] }, .. SettingsSections]
    };
}
