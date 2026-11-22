using FluxFlow.Components.Bluetooth;

namespace FluxFlow.Plugins.Bluetooth.Design;

/// <summary>Owns a device selection session; the caller commits a confirmed selection to its project.</summary>
public sealed partial class BleTargetBrowser : ISelectionExplorer
{
    private readonly Func<string, BleClientOptions>? _resolveConnection;
    private readonly Func<string, string?> _connectionName;
    private readonly Func<bool>? _isCurrent;
    public bool DeviceOnly { get; }
    public BleClientDiscoverySession Discovery { get; }
    public bool Named => _named;
    public bool Scanning => _scanning;
    public bool DeviceExpanded => _deviceExpanded;
    public string Address => _address;
    public string Connection => _connection;
    public BleAddressType AddressType => _addressType;
    public long Version => _targetVersion;
    public GattSelection? Selection => _selection;
    public string? Error => _error ?? Discovery.Error;
    public bool DiscoveryRequested => _discoveryTarget.HasValue;

    public BleTargetBrowser(IBleExplorer explorer, BleTargetSelection initial, bool named, bool deviceOnly,
        Func<string, BleClientOptions>? resolveConnection, Func<string, string?> connectionName, Func<bool>? isCurrent = null)
    {
        Discovery = new(explorer);
        DeviceOnly = deviceOnly;
        _resolveConnection = resolveConnection;
        _connectionName = connectionName;
        _isCurrent = isCurrent;
        _named = named || !string.IsNullOrWhiteSpace(initial.Connection);
        _address = initial.DeviceAddress;
        _addressType = initial.AddressType;
        _connection = initial.Connection;
        ResolveNamed();
    }

    private bool _closed, _named, _scanning;
    private bool _deviceExpanded;
    private long _targetVersion;
    private string _address = "", _connection = "";
    private BleAddressType _addressType;
    private BleClientOptions? _resolved;
    private BleDeviceInfo? _selectedDevice;
    public sealed record GattSelection(Guid ServiceId, Guid? CharacteristicId, BleDeviceInfo? Device = null) : ExplorerSelection;
    private GattSelection? _selection;
    private (string Address, BleAddressType Type)? _discoveryTarget;
    private string? _error;
    public IEnumerable<BleDeviceInfo> Devices
    {
        get
        {
            var scanned = _named ? [] : Discovery.Devices;
            if (Device is not { } current) return scanned;
            if (scanned.Any(IsActive)) return scanned;
            var selected = _selectedDevice is { } known && IsActive(known) ? known : new BleDeviceInfo(current.DeviceAddress, current.AddressType,
                _named ? _connectionName(_connection) ?? "Peripheral connection" : "Device", 0);
            return scanned.Prepend(selected);
        }
    }
    public IReadOnlyList<ExplorerItem<ExplorerSelection>> Roots()
    {
        return Devices.Select(device =>
        {
            var active = IsActive(device);
            var item = new ExplorerItem<ExplorerSelection>(new GattSelection(Guid.Empty, null, device),
                string.IsNullOrWhiteSpace(device.Name) ? device.Address : device.Name, ExplorerGlyph.Device)
                { Hint = $"{device.Address} · {device.AddressType}" };
            if (DeviceOnly) return item;
            IReadOnlyList<ExplorerItem<ExplorerSelection>> services;
            if (active && Discovery.Services.Length > 0)
                services = Discovery.Services.Select(service => new ExplorerItem<ExplorerSelection>(
                    new GattSelection(service.Id, null), UuidLabel(service.Id, BleUuidKind.Service), ExplorerGlyph.Folder)
                {
                    Hint = service.Id.ToString(), Expanded = true, Detail = service.Characteristics.Length == 0 ? "No characteristics" : null,
                    Children = service.Characteristics.Select(characteristic => new ExplorerItem<ExplorerSelection>(
                        new GattSelection(service.Id, characteristic.Id), UuidLabel(characteristic.Id, BleUuidKind.Characteristic), ExplorerGlyph.Value)
                        { Hint = characteristic.Id.ToString() }).ToArray()
                }).ToArray();
            else
            {
                var status = !active ? "Select to discover services" : Error is not null ? "Discovery failed. Refresh to retry."
                    : Discovery.Busy ? "Discovering services…" : DiscoveryRequested ? "No services found. Refresh to retry." : "Select to discover services";
                services = [new(null, status, ExplorerGlyph.None)];
            }
            return item with { Expanded = active && DeviceExpanded, LoadOnExpand = true, Children = services };
        }).ToArray();
    }

    public bool IsActive(BleDeviceInfo device) => Device is { } current && device.Address == current.DeviceAddress && device.AddressType == current.AddressType;
    public async Task ExpandDevice(BleDeviceInfo device, bool expanded, long version)
    {
        if (!Current || version != _targetVersion) return;
        if (!expanded) { if (IsActive(device)) _deviceExpanded = false; return; }
        await ActivateDevice(device);
    }
    private async Task ActivateDevice(BleDeviceInfo device)
    {
        if (_scanning) CancelOperation();
        _deviceExpanded = true;
        if (IsActive(device))
        {
            if (DeviceOnly) _selection = new(Guid.Empty, null, device);
            await DiscoverTarget();
            return;
        }
        await ChooseDevice(device);
    }
    public static string UuidLabel(Guid id, BleUuidKind kind) => BleStandards.Entries.FirstOrDefault(entry => entry.Id == id && entry.Kind == kind)?.Name ?? id.ToString();
    public BleServiceInfo? SelectedService => Discovery.Services.FirstOrDefault(item => item.Id == _selection?.ServiceId);
    public BleCharacteristicInfo? SelectedCharacteristic => SelectedService?.Characteristics.FirstOrDefault(item => item.Id == _selection?.CharacteristicId);
    public string DeviceName => Devices.FirstOrDefault(IsActive) is { } device && !string.IsNullOrWhiteSpace(device.Name) ? device.Name : "Device";
    private bool Current => !_closed && _isCurrent?.Invoke() != false;
    public BleClientOptions? Device
    {
        get
        {
            if (_named) return _resolved;
            var device = new BleClientOptions { DeviceAddress = _address, AddressType = _addressType };
            try { device.ValidateDevice(); return device; } catch (ArgumentException) { return null; }
        }
    }
    public bool CanConfirm => Current && !Discovery.Busy && Device is not null &&
        (DeviceOnly || Discovery.Services.Any(service => service.Id == _selection?.ServiceId && service.Characteristics.Any(item => item.Id == _selection.CharacteristicId)));
    private void ResetTarget() { _targetVersion++; _scanning = false; Discovery.ClearTarget(); _selection = null; _discoveryTarget = null; _error = null; _resolved = null; _deviceExpanded = false; }
    private void ResolveNamed()
    {
        if (!_named || string.IsNullOrWhiteSpace(_connection)) return;
        try { _resolved = _resolveConnection?.Invoke(_connection) ?? throw new ArgumentException("Select a peripheral connection available in this environment."); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException) { _error = error.Message; }
    }
    public async Task ChangeSource(bool value) { if (!Current) return; ResetTarget(); _named = value; ResolveNamed(); await DiscoverTarget(); }
    public async Task ChangeConnection(string value) { if (!Current) return; ResetTarget(); _connection = value; ResolveNamed(); await DiscoverTarget(); }
    public void ChangeAddress(string value) { if (!Current || _address == value) return; ResetTarget(); _address = value; _selectedDevice = null; }
    public async Task ChangeAddressType(BleAddressType value) { if (!Current) return; ResetTarget(); _addressType = value; _selectedDevice = null; await DiscoverTarget(); }
    private async Task ChooseDevice(BleDeviceInfo? device)
    {
        if (device is null) { _selectedDevice = null; return; }
        ResetTarget(); _selectedDevice = device; _address = device.Address; _addressType = device.AddressType;
        _deviceExpanded = true;
        if (DeviceOnly) _selection = new(Guid.Empty, null, device);
        await DiscoverTarget();
    }
    public async Task ChooseSelection(GattSelection? value, long version)
    {
        if (!Current || version != _targetVersion) return;
        if (value?.Device is { } device) { _selection = null; await ActivateDevice(device); return; }
        if (Discovery.Busy) return;
        _selection = value;
    }
    public async Task Scan()
    {
        if (!Current || _named) return;
        if (Discovery.Busy) { if (_scanning) CancelOperation(); return; }
        ResetTarget();
        var version = _targetVersion;
        _scanning = true;
        try
        {
            await Discovery.ScanAsync(() => Current);
        }
        finally { if (version == _targetVersion) _scanning = false; }
    }
    private void CancelOperation()
    {
        _targetVersion++;
        _scanning = false;
        _discoveryTarget = null;
        Discovery.Cancel();
    }
    public async Task Connect()
    {
        if (!Current || DeviceOnly) return;
        if (Discovery.Busy)
        {
            if (!_scanning) CancelOperation();
            return;
        }
        _discoveryTarget = null;
        await DiscoverTarget();
    }
    public async Task DiscoverTarget()
    {
        if (!Current || DeviceOnly || _scanning || Device is not { } device) return;
        var target = (device.DeviceAddress, device.AddressType);
        if (_discoveryTarget == target) return;
        Discovery.ClearTarget(); _error = null; _selection = null;
        _deviceExpanded = true;
        _discoveryTarget = target;
        var version = ++_targetVersion;
        await Discovery.DiscoverAsync(device, () => Current && version == _targetVersion);
    }
    public BleTargetSelection? Confirm() => !CanConfirm ? null :
        new(_named ? _connection : "", _address, _addressType, DeviceOnly ? null : _selection?.ServiceId,
            DeviceOnly ? null : _selection?.CharacteristicId);

    public void Dispose()
    {
        _closed = true;
        Discovery.Dispose();
    }
}
