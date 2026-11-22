using FluxFlow.Authoring;
using FluxFlow.Components.Bluetooth;

namespace FluxFlow.Plugins.Bluetooth.Design;

public sealed partial class BleTargetBrowser
{
    public string TreeLabel => "Bluetooth devices and GATT services";
    public string EmptyTitle => Scanning ? "Scanning for devices…" : "Choose a Bluetooth device";
    public string EmptyHint => "Scan nearby devices or enter a known address.";
    public string SelectionSummary => !CanConfirm ? "No target selected" : DeviceOnly ? "1 device selected" : "1 characteristic selected";
    public string? ManualEntryLabel => Named ? null : "Enter address…";
    public bool Busy => Discovery.Busy;
    public string? Status => Discovery.Status;
    ExplorerSelection? ISelectionExplorer.Selection => Selection;
    object? ISelectionExplorer.Confirm() => Confirm();
    public Task Select(ExplorerSelection? item, long version) => ChooseSelection(item as GattSelection, version);
    public Task Expand(ExplorerSelection item, bool expanded, long version) => item is GattSelection { Device: { } device }
        ? ExpandDevice(device, expanded, version) : Task.CompletedTask;

    public ExplorerField Source => new("Device source", Named ? "connection" : "direct", next => ChangeSource(next == "connection"))
    {
        Choices = [new() { Value = "direct", DisplayName = "Direct device" }, new() { Value = "connection", DisplayName = "Named connection" }]
    };
    public IReadOnlyList<ExplorerField> Inputs => Named
        ? [new("Peripheral connection", Connection, ChangeConnection) { ResourceType = "ble.peripheral",
            NoConnectionsHint = "No BLE peripheral connections. Add one from Connections, or choose Direct device." }]
        : [new("Device address", Address, value => { ChangeAddress(value); return Task.CompletedTask; })
            { FinishEntry = DiscoverTarget, Placeholder = "Enter a known device address" },
            new("Address type", AddressType.ToString(), value => Enum.TryParse<BleAddressType>(value, out var type) && Enum.IsDefined(type)
                ? ChangeAddressType(type) : Task.CompletedTask)
            { Choices = Enum.GetValues<BleAddressType>().Select(type => OptionChoiceModel.FromEnum(type)).ToArray() }];
    public IReadOnlyList<ExplorerCommand> Commands =>
    [
        new(Scanning && Busy ? "Cancel scan" : "Scan devices", Scanning && Busy ? ExplorerCommandKind.Cancel : ExplorerCommandKind.Search,
            Scan, !Named && (!Busy || Scanning)),
        new(Busy && !Scanning ? "Cancel discovery" : "Refresh services", Busy && !Scanning ? ExplorerCommandKind.Cancel : ExplorerCommandKind.Refresh,
            Connect, !DeviceOnly && !(Busy && Scanning) && Device is not null, Compact: true)
    ];

    public ExplorerDetails Details
    {
        get
        {
            if (Device is not { } device)
                return new("Target details", DeviceOnly ? "Select a device to inspect its address." : "Select a characteristic to inspect its target.", []);
            var fields = new List<KeyValuePair<string, string>> { new("Device", DeviceName), new("Address", device.DeviceAddress), new("Address type", device.AddressType.ToString()) };
            var title = DeviceName;
            var hint = DeviceOnly ? "Selected device" : "Select a service and characteristic";
            if (SelectedService is { } service)
            {
                title = UuidLabel(service.Id, BleUuidKind.Service); hint = "Select a characteristic";
                fields.Add(new("Service", title)); fields.Add(new("Service UUID", service.Id.ToString()));
            }
            if (SelectedCharacteristic is { } characteristic)
            {
                title = UuidLabel(characteristic.Id, BleUuidKind.Characteristic); hint = "Selected characteristic";
                fields.Add(new("Characteristic UUID", characteristic.Id.ToString())); fields.Add(new("Properties", characteristic.Properties));
            }
            return new(title, hint, fields);
        }
    }
}
