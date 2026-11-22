using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Components.Bluetooth;

public sealed record BleServerUpdate
{
    public Guid ServiceId { get; init; }
    public Guid CharacteristicId { get; init; }
    public required BlePayload Value { get; init; }
    public bool Notify { get; init; }
    public static BleServerUpdate FromJson(JsonElement value) => value.Deserialize(BleServerJsonContext.Default.BleServerUpdate)
        ?? throw new ArgumentException("Expected a server value update.");
}

public sealed class BleServerValue(Guid serviceId, Guid characteristicId, byte[] value, string operation) : FlowValue
{
    public Guid ServiceId { get; } = serviceId;
    public Guid CharacteristicId { get; } = characteristicId;
    public string Operation { get; } = operation;
    public string ValueBase64 { get; } = Convert.ToBase64String(value);
    public int ByteLength { get; } = value.Length;
    protected override JsonElement CreateJson() => JsonSerializer.SerializeToElement(this, BleServerJsonContext.Default.BleServerValue);
}

// Values and callbacks belong to one run. The configuration is never modified.
public sealed class BleServerSession
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid, Guid), (BleCharacteristic Definition, byte[] Value)> _values;
    private readonly Func<BleServerValue, Task> _written;
    private readonly CancellationToken _stopping;
    private bool _closed;
    private Func<Guid, Guid, byte[], Task>? _notify;
    public BleServerSession(BleServerOptions options, Func<BleServerValue, Task> written, CancellationToken stopping)
    {
        _written = written;
        _stopping = stopping;
        _values = options.GetServices().SelectMany(service => service.Characteristics.Select(item =>
            KeyValuePair.Create((service.Id, item.Id), (item, item.GetBytes())))).ToDictionary();
    }
    public void SetNotifier(Func<Guid, Guid, byte[], Task> notify) { lock (_gate) { RequireOpen(); _notify = notify; } }
    public void Close() { lock (_gate) { _closed = true; _notify = null; } }
    private void RequireOpen()
    {
        _stopping.ThrowIfCancellationRequested();
        if (_closed) throw new OperationCanceledException("The BLE server session has ended.");
    }
    public byte[] Read(Guid service, Guid characteristic)
    {
        lock (_gate) { RequireOpen(); return _values[(service, characteristic)].Value.ToArray(); }
    }
    public async Task ReceiveWriteAsync(Guid service, Guid characteristic, byte[] bytes)
    {
        if (bytes.Length > 512) throw new ArgumentException("A characteristic value cannot exceed 512 bytes.");
        if (BleStandards.HasCodec(characteristic)) _ = BleStandards.Decode(characteristic, bytes);
        lock (_gate)
        {
            RequireOpen();
            var current = _values[(service, characteristic)];
            if ((current.Definition.Properties & (BleCharacteristicProperties.Write | BleCharacteristicProperties.WriteWithoutResponse)) == 0)
                throw new InvalidOperationException("This characteristic is not writable.");
            _values[(service, characteristic)] = (current.Definition, bytes.ToArray());
        }
        _stopping.ThrowIfCancellationRequested();
        await _written(new(service, characteristic, bytes, "Write")).ConfigureAwait(false);
    }
    public async Task<BleServerValue> UpdateAsync(BleServerUpdate update)
    {
        var bytes = (update.Value ?? throw new ArgumentException("Value is required.")).Decode();
        if (BleStandards.HasCodec(update.CharacteristicId)) _ = BleStandards.Decode(update.CharacteristicId, bytes);
        Func<Guid, Guid, byte[], Task>? notify;
        lock (_gate)
        {
            RequireOpen();
            if (!_values.TryGetValue((update.ServiceId, update.CharacteristicId), out var current)) throw new ArgumentException("The service/characteristic is not in this server.");
            if (update.Notify && (current.Definition.Properties & (BleCharacteristicProperties.Notify | BleCharacteristicProperties.Indicate)) == 0)
                throw new ArgumentException("Enable Notify or Indicate on this characteristic before publishing notifications.");
            notify = _notify;
            if (update.Notify && notify is null) throw new InvalidOperationException("Server notifications are not ready.");
            _values[(update.ServiceId, update.CharacteristicId)] = (current.Definition, bytes.ToArray());
        }
        if (update.Notify) await notify!(update.ServiceId, update.CharacteristicId, bytes).ConfigureAwait(false);
        lock (_gate) RequireOpen();
        return new(update.ServiceId, update.CharacteristicId, bytes, "Updated");
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(BleServerUpdate))]
[JsonSerializable(typeof(BleServerValue))]
internal partial class BleServerJsonContext : JsonSerializerContext;
