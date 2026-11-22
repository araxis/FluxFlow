using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Components.Bluetooth;

namespace FluxFlow.Plugins.Bluetooth.Design;

internal sealed class BleTargetInspector(IBleExplorer explorer, NodeInspectorModel inspector) : IInteractiveInspector
{
    private readonly BleClientDiscoverySession _session = new(explorer);
    private IDictionary<string, JsonElement> _settings = null!;
    private (string Connection, string Address, string AddressType, string Type) _target;
    private (string Service, string Characteristic, bool Dynamic) _operation;
    private long _revision = -1;
    private string? _readError;
    private bool _reading;
    private string Text(string field) => BleClientProperties.Text(_settings, field);
    private bool Named => !string.IsNullOrWhiteSpace(Text("Connection"));
    private bool Dynamic => BleClientProperties.InputOperation(_settings);
    private bool CanRead => !Named && (!Dynamic || Text("Type") == "ble.notifications");

    public void Refresh(IDictionary<string, JsonElement> settings, long revision)
    {
        _settings = settings;
        var target = (Text("Connection"), Text("DeviceAddress"), Text("AddressType"), Text("Type"));
        var operation = (Text("ServiceId"), Text("CharacteristicId"), Dynamic);
        if (target != _target) _session.ClearTarget();
        else if (revision != _revision || operation != _operation) _session.ClearValue();
        if (_revision != revision || target != _target || operation != _operation) { _readError = null; _reading = false; }
        (_revision, _target, _operation) = (revision, target, operation);
    }

    public InspectorResource Resource => new("Peripheral connection", "ble.peripheral", Text("Connection"), "Direct device",
        $"BLE peripheral connection '{Text("Connection")}' is unavailable. Choose an existing connection or use a direct device.",
        "No BLE peripheral connections. Add one from the Connections menu, or choose Direct device.",
        reference => BleClientProperties.Set(_settings, "Connection", reference));

    public IEnumerable<InspectorSection> Sections => inspector.Sections.Select(section =>
        new InspectorSection(section.Name, Rows(section), section.Name == "Device target")).Where(section => section.Rows.Any());

    private IEnumerable<InspectorRow> Rows(OptionSectionModel section)
    {
        var target = section.Name == "Device target";
        if (target) yield return new InspectorSummary("Device source", Named ? "Named connection" : "Direct device");
        foreach (var option in section.Options.Where(option => BleClientProperties.IsVisible(_settings, option.Name)))
        {
            if (option.Name == "Connection") yield return Resource;
            else if (option.Name == "UseInputOperation")
            {
                yield return new InspectorMode("Input mode", Dynamic, "Fixed operation", "Command from message", value => BleClientProperties.Set(_settings, option.Name, value));
                if (Dynamic) yield return new InspectorExample("Command message example", CommandExample);
            }
            else if (option.Name == "Value" && BleClientProperties.Payload(_settings) is { } payload)
                yield return new InspectorBytes(BleValueFormat.ToEditor(payload), BleValueFormat.For(Text("CharacteristicId")), value =>
                    BleClientProperties.SetPayload(_settings, BleValueFormat.FromEditor(value)));
            else yield return new InspectorOption(option, target ? Text(option.Name) : null);
        }
    }

    public InspectorPicker Picker => new("Select BLE target", context =>
        new BleTargetBrowser(explorer, new(Text("Connection"), Text("DeviceAddress"),
            BleClientProperties.EnumValue<BleAddressType>(_settings, "AddressType") ?? BleAddressType.Public),
            Named, Dynamic && Text("Type") == "ble.client", reference => BleDiscoveryAddress.Resolve(context.Endpoints(reference)),
            context.Name, context.Current), result => ((BleTargetSelection)result).Apply(_settings));

    public InspectorPreview Preview => new("Read characteristic",
        "Read from the configured device, service UUID and characteristic UUID. This does not run the workflow or change its configuration.",
        [new("Device", Text("DeviceAddress")), new("Service UUID", Text("ServiceId")), new("Characteristic UUID", Text("CharacteristicId"))],
        "Read preview", CanRead && !_session.Busy, Read,
        Named ? "Read preview requires a direct device address. Environment-bound connections are resolved at Run." :
            !CanRead ? "In command mode, the input message supplies the characteristic to read." : null,
        _reading ? _session.Status : null, _readError, _session.Value, "preview only; no workflow message sent.",
        _session.Busy ? _session.Cancel : null, "Cancel read");

    private async Task Read(Func<bool> current)
    {
        if (!CanRead || _session.Busy) return;
        _reading = true; _readError = null;
        try
        {
            await _session.ReadAsync(BleClientProperties.PreviewOptions(_settings), current);
            if (current()) _readError = _session.Error;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or JsonException)
        { if (current()) _readError = error.Message; }
    }
    public void Dispose() => _session.Dispose();
    private const string CommandExample = """
        {
          "Operation": "Write",
          "ServiceId": "3f63b38e-98df-4a52-a9f8-76b803a934b2",
          "CharacteristicId": "f50d7529-4e5d-4088-b88f-e236cd1e7f29",
          "WriteMode": "WithResponse",
          "Value": { "Encoding": "Hex", "Data": "01" }
        }
        """;
}
