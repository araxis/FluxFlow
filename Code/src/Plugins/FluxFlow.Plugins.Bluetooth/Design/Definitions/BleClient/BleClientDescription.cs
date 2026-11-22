using FluxFlow.Components.Bluetooth;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.BleClient;

public sealed class BleClientDescription : IComponentDescription
{
    internal static IReadOnlyList<OptionEditorModel> CharacteristicOptions { get; } = [
        Create("ServiceId", "Service UUID", help: "Full 128-bit GATT service UUID; incoming operations supply their own UUIDs.") with { IsRequired = true, ValidateText = ValidateUuid },
        Create("CharacteristicId", "Characteristic UUID", help: "Target GATT characteristic UUID; incoming operations supply their own UUIDs.") with { IsRequired = true, ValidateText = ValidateUuid }
    ];
    internal const string BluetoothIcon = """<path d="M17.71 7.71 12 2h-1v7.59L6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 11 14.41V22h1l5.71-5.71L13.41 12l4.3-4.29zM13 5.83l1.88 1.88L13 9.59V5.83zm1.88 10.46L13 18.17v-3.76l1.88 1.88z"/>""";
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "ble.client", DisplayName = "BLE Client", Category = "Devices", PreferredNodeName = "ble-client",
        Icon = BluetoothIcon, DefaultColor = "#68a7ef",
        Summary = "Reads or writes BLE GATT characteristics using configured settings or incoming operation messages on the Windows companion.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output }, new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };
    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "ble.client", PrimaryEditor = "DeviceAddress", Sections = [new() { Name = "Device target", Options = [
            Create("Connection", "Peripheral connection", help: "Optional named ble.peripheral connection. Environment bindings supply its address at run time.") with { RelatedResource = "ble.peripheral" },
            Create("DeviceAddress", "Device address", help: "Used when no peripheral connection is selected. No automatic pairing or nearby-device selection.") with { IsRequired = true, ValidateText = ValidateDeviceAddress },
            Create("AddressType", "Address type", OptionEditorKind.Select, "Use the address type advertised by the device.", "Public") with
            { Choices = [OptionChoiceModel.FromEnum(BleAddressType.Public), OptionChoiceModel.FromEnum(BleAddressType.Random)] },
            .. CharacteristicOptions,
        ] }, new() { Name = "Operation", Options = [
            Create("UseInputOperation", "Operation from input", OptionEditorKind.Toggle, "Read Operation, ServiceId, CharacteristicId, WriteMode and Value from each input message.", false),
            Create("Operation", "Operation", OptionEditorKind.Select, help: "Read a characteristic or write the configured value.", defaultValue: "Read") with { Choices = [OptionChoiceModel.FromEnum(BleOperationKind.Read), OptionChoiceModel.FromEnum(BleOperationKind.Write)] },
            Create("WriteMode", "Write mode", OptionEditorKind.Select, help: "With response waits for the GATT write response; without response does not confirm device processing.", defaultValue: "WithResponse") with { Choices = [OptionChoiceModel.FromEnum(BleWriteMode.WithResponse, "With response"), OptionChoiceModel.FromEnum(BleWriteMode.WithoutResponse, "Without response")] },
            Create("Value", "Write value", OptionEditorKind.Json, "Object with Encoding (Hex, Base64 or Utf8) and Data.")
        ] }, new() { Name = "Execution", Options = [
            Create("TimeoutSeconds", "Timeout (seconds)", OptionEditorKind.Integer, "Requests cancellation. Windows may need additional time to finish native I/O and release handles.", 20) with { Min = 1, Max = 300 },
            Create("OutputCapacity", "Output capacity", OptionEditorKind.Integer, help: "Maximum number of buffered output messages.", defaultValue: 128) with { Min = 1, IsAdvanced = true, Max = int.MaxValue }
        ] }]
    };
    private static string? ValidateUuid(string? text) =>
        Guid.TryParse(text, out var id) && id != Guid.Empty ? null : "Enter a nonempty service or characteristic UUID.";

    private static string? ValidateDeviceAddress(string? text)
    {
        try { new BleClientOptions { DeviceAddress = text ?? "" }.ValidateDevice(); return null; }
        catch (ArgumentException error) { return error.Message; }
    }
}
