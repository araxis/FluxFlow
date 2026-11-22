using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Components.Bluetooth;

public sealed class BleWriteResult(int byteLength, BleWriteMode writeMode) : FlowValue
{
    public int ByteLength { get; } = byteLength;
    public string WriteMode { get; } = writeMode.ToString();
    public bool Acknowledged { get; } = writeMode == BleWriteMode.WithResponse;
    protected override JsonElement CreateJson() => JsonSerializer.SerializeToElement(this, BleWriteResultJsonContext.Default.BleWriteResult);
}

[JsonSerializable(typeof(BleWriteResult))]
internal partial class BleWriteResultJsonContext : JsonSerializerContext;
