using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Components.Bluetooth;

public enum BleOperationKind { Read, Write }
public enum BleWriteMode { WithResponse, WithoutResponse }
public enum BleValueEncoding { Hex, Base64, Utf8 }
public enum BleSubscriptionMode { Notify, Indicate }

public sealed record BlePayload
{
    public BleValueEncoding Encoding { get; init; }
    public string Data { get; init; } = "";
    public byte[] Decode()
    {
        ArgumentNullException.ThrowIfNull(Data);
        if (Data.Length > 4096) throw new ArgumentException("BLE values must contain at most 512 bytes.");
        var bytes = Encoding switch
        {
            BleValueEncoding.Hex => Convert.FromHexString(Data),
            BleValueEncoding.Base64 => Convert.FromBase64String(Data),
            BleValueEncoding.Utf8 => new UTF8Encoding(false, true).GetBytes(Data),
            _ => throw new ArgumentException("Unknown BLE value encoding.")
        };
        if (bytes.Length > 512) throw new ArgumentException("BLE values must contain at most 512 bytes. The device may impose a lower limit.");
        return bytes;
    }
}

public sealed record BleOperation
{
    public required BleOperationKind Operation { get; init; }
    public Guid ServiceId { get; init; }
    public Guid CharacteristicId { get; init; }
    public BleWriteMode WriteMode { get; init; }
    public BlePayload? Value { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Operation) || !Enum.IsDefined(WriteMode)) throw new ArgumentException("Invalid BLE operation or write mode.");
        if (ServiceId == Guid.Empty || CharacteristicId == Guid.Empty) throw new ArgumentException("Service and characteristic UUIDs are required.");
        if (Operation == BleOperationKind.Write)
            _ = (Value ?? throw new ArgumentException("A write requires a Value with Encoding and Data.")).Decode();
    }

    public static BleOperation FromJson(JsonElement value) => value.Deserialize(BleOperationJsonContext.Default.BleOperation)
        ?? throw new ArgumentException("Input must contain a BLE operation object.");
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(BleOperation))]
internal partial class BleOperationJsonContext : JsonSerializerContext;
