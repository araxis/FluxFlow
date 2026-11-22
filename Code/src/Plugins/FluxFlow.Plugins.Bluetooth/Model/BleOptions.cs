using System.Globalization;
using System.Text;

namespace FluxFlow.Components.Bluetooth;

public enum BleAddressType { Public, Random }

public sealed record BleClientOptions
{
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
    public BleOperation ConfiguredOperation => new() { Operation = Operation, ServiceId = ServiceId, CharacteristicId = CharacteristicId, WriteMode = WriteMode, Value = Value };
    public ulong Address => ulong.Parse(DeviceAddress.Replace(":", "").Replace("-", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    public void Validate()
    {
        ValidateDevice();
        if (!UseInputOperation) ConfiguredOperation.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OutputCapacity);
    }
    public void ValidateSubscription()
    {
        ValidateDevice();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OutputCapacity);
        if (!Enum.IsDefined(SubscriptionMode)) throw new ArgumentException("Subscription mode must be Notify or Indicate.");
        if (ServiceId == Guid.Empty || CharacteristicId == Guid.Empty) throw new ArgumentException("Service and characteristic UUIDs are required.");
    }
    public void ValidateDevice()
    {
        var text = DeviceAddress?.Replace(":", "").Replace("-", "") ?? "";
        if (text.Length != 12 || !text.All(Uri.IsHexDigit) || !ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address) || address == 0)
            throw new ArgumentException("Device address must contain twelve hexadecimal digits; colons or hyphens are optional.");
        if (!Enum.IsDefined(AddressType)) throw new ArgumentException("Address type must be Public or Random.");
        if (TimeoutSeconds is < 1 or > 300) throw new ArgumentException("Timeout must be between 1 and 300 seconds.");
    }
}

public sealed record BleCharacteristic
{
    public Guid Id { get; init; }
    public string Value { get; init; } = "";
    public BlePayload? InitialValue { get; init; }
    public BleCharacteristicProperties Properties { get; init; } = BleCharacteristicProperties.Read;
    public BleAccessProtection ReadProtection { get; init; }
    public BleAccessProtection WriteProtection { get; init; }
    public string UserDescription { get; init; } = "";
    public BleDescriptor[] Descriptors { get; init; } = [];
    public byte[] GetBytes() => InitialValue?.Decode() ?? new BlePayload { Encoding = BleValueEncoding.Utf8, Data = Value }.Decode();
}

public sealed record BleServerOptions
{
    public Guid ServiceId { get; init; }
    public BleCharacteristic[] Characteristics { get; init; } = [];
    public BleService[]? Services { get; init; }
    public bool IsConnectable { get; init; } = true;
    public bool IsDiscoverable { get; init; } = true;
    public int OutputCapacity { get; init; } = 128;
    public BleService[] GetServices() => Services ?? [new() { Id = ServiceId, Characteristics = Characteristics }];
    public BleServerOptions Snapshot() => this with
    {
        Characteristics = Characteristics?.Select(CopyCharacteristic).ToArray() ?? [],
        Services = Services?.Select(service => service with { Characteristics = service.Characteristics.Select(CopyCharacteristic).ToArray() }).ToArray()
    };
    private static BleCharacteristic CopyCharacteristic(BleCharacteristic value) => value with { Descriptors = value.Descriptors.ToArray() };
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OutputCapacity);
        var services = GetServices();
        if (Services is not null && (ServiceId != Guid.Empty || Characteristics is { Length: > 0 }))
            throw new ArgumentException("Use Services or the legacy ServiceId/Characteristics fields, not both.");
        if (services.Length is < 1 or > 8) throw new ArgumentException("Define between 1 and 8 primary services.");
        var serviceIds = new HashSet<Guid>();
        foreach (var service in services)
        {
            if (service is null || service.Id == Guid.Empty || !serviceIds.Add(service.Id)) throw new ArgumentException("Each service needs a unique, nonempty UUID.");
            if (BleStandards.IsSystemService(service.Id)) throw new ArgumentException("This service is reserved by Windows and cannot be published by the companion.");
            if (service.Characteristics is null || service.Characteristics.Length is < 1 or > 32) throw new ArgumentException("Define between 1 and 32 characteristics per service.");
            var ids = new HashSet<Guid>();
            foreach (var item in service.Characteristics)
            {
                if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id)) throw new ArgumentException("Each characteristic needs a unique, nonempty UUID within its service.");
                if (item.Properties == 0 || (item.Properties & ~BleCharacteristicProperties.Supported) != 0) throw new ArgumentException("Unsupported characteristic properties.");
                if (!Enum.IsDefined(item.ReadProtection) || !Enum.IsDefined(item.WriteProtection)) throw new ArgumentException("Unknown access protection.");
                var bytes = item.GetBytes();
                if (BleStandards.HasCodec(item.Id)) _ = BleStandards.Decode(item.Id, bytes);
                if (item.UserDescription is null || Encoding.UTF8.GetByteCount(item.UserDescription) > 512) throw new ArgumentException("User description is limited to 512 UTF-8 bytes.");
                if (item.Descriptors is null || item.Descriptors.Length > 16) throw new ArgumentException("At most 16 custom descriptors are supported per characteristic.");
                var descriptors = new HashSet<Guid>();
                foreach (var descriptor in item.Descriptors)
                {
                    if (descriptor is null || descriptor.Id == Guid.Empty || !descriptors.Add(descriptor.Id)) throw new ArgumentException("Descriptor UUIDs must be nonempty and unique.");
                    if (BleStandards.IsBluetoothUuid(descriptor.Id)) throw new ArgumentException("Use User description for 0x2901. Other standard descriptors are system-managed or unsupported; custom descriptors require a custom 128-bit UUID.");
                    if (!Enum.IsDefined(descriptor.ReadProtection)) throw new ArgumentException("Unknown descriptor read protection.");
                    _ = (descriptor.Value ?? throw new ArgumentException("Descriptor value is required.")).Decode();
                }
            }
        }
    }
}

