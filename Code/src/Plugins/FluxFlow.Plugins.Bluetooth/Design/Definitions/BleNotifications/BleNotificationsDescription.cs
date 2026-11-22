using FluxFlow.Components.Bluetooth;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.BleNotifications;

public sealed class BleNotificationsDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "ble.notifications", DisplayName = "BLE Notifications", Category = "Devices", PreferredNodeName = "ble-notifications",
        Icon = BleClient.BleClientDescription.BluetoothIcon, DefaultColor = "#68a7ef",
        Summary = "Subscribes to a BLE characteristic and emits original notification or indication bytes until Stop.",
        Outputs = [new() { Name = "Output", Kind = PortKind.Output }, new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };
    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "ble.notifications", PrimaryEditor = "DeviceAddress",
        Sections = [new BleClient.BleClientDescription().Inspector.Sections[0],
            new() { Name = "Subscription", Options = [
                Create("SubscriptionMode", "Subscription mode", OptionEditorKind.Select, help: "Select notifications or acknowledged indications supported by the characteristic.", defaultValue: "Notify") with
                { Choices = [OptionChoiceModel.FromEnum(BleSubscriptionMode.Notify, "Notifications"), OptionChoiceModel.FromEnum(BleSubscriptionMode.Indicate, "Indications")] }
            ] },
            new BleClient.BleClientDescription().Inspector.Sections.Last()]
    };
}
