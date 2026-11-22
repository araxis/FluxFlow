namespace FluxFlow.Components.Bluetooth;

[Flags]
public enum BleCharacteristicProperties
{
    Read = 2, WriteWithoutResponse = 4, Write = 8, Notify = 16, Indicate = 32,
    Supported = Read | WriteWithoutResponse | Write | Notify | Indicate
}

public enum BleAccessProtection { Plain, AuthenticationRequired, EncryptionRequired, EncryptionAndAuthenticationRequired }

public sealed record BleService
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public BleCharacteristic[] Characteristics { get; init; } = [];
}

public sealed record BleDescriptor
{
    public Guid Id { get; init; }
    public BlePayload Value { get; init; } = new();
    public BleAccessProtection ReadProtection { get; init; }
}
