using FluxFlow.Components.Bluetooth;

namespace FluxFlow.Configuration.Components.Bluetooth;

public sealed record BleClientConfiguration : OutputComponentConfiguration
{
    public string? Connection { get; init; }
    public string DeviceAddress { get; init; } = "";
    public BleAddressType AddressType { get; init; }
    public Guid ServiceId { get; init; }
    public Guid CharacteristicId { get; init; }
    public int TimeoutSeconds { get; init; } = 20;
    public int OutputCapacity { get; init; } = 128;
    public bool UseInputOperation { get; init; }
    public BleOperationKind Operation { get; init; }
    public BleWriteMode WriteMode { get; init; }
    public BlePayload? Value { get; init; }
    public BleSubscriptionMode SubscriptionMode { get; init; }
    public BleClientOptions ToOptions() => new() { DeviceAddress = DeviceAddress, AddressType = AddressType, ServiceId = ServiceId,
        CharacteristicId = CharacteristicId, TimeoutSeconds = TimeoutSeconds, OutputCapacity = OutputCapacity,
        UseInputOperation = UseInputOperation, Operation = Operation, WriteMode = WriteMode, Value = Value, SubscriptionMode = SubscriptionMode };
    public void Validate(bool subscription)
    {
        var options = ToOptions();
        if (Connection is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(Connection);
            // The named resource supplies the address when the run's binding snapshot is resolved.
            options = options with { DeviceAddress = "000000000001" };
        }
        if (subscription) options.ValidateSubscription(); else options.Validate();
    }
}

public sealed record BleServerConfiguration : OutputComponentConfiguration
{
    public string? Adapter { get; init; }
    public Guid ServiceId { get; init; }
    public BleCharacteristic[] Characteristics { get; init; } = [];
    public BleService[]? Services { get; init; }
    public int OutputCapacity { get; init; } = 128;
    public bool IsConnectable { get; init; } = true;
    public bool IsDiscoverable { get; init; } = true;
    public BleServerOptions ToOptions() => new() { ServiceId = ServiceId, Characteristics = Characteristics,
        IsConnectable = IsConnectable, IsDiscoverable = IsDiscoverable, Services = Services, OutputCapacity = OutputCapacity };
}
