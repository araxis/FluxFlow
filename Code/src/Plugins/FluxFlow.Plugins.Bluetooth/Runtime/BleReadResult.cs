using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Components.Bluetooth;

public sealed class BleReadResult(string valueBase64, int byteLength) : FlowValue
{
    public string ValueBase64 { get; } = valueBase64;
    public int ByteLength { get; } = byteLength;
    protected override JsonElement CreateJson() => JsonSerializer.SerializeToElement(this, BleJsonContext.Default.BleReadResult);
}

[JsonSerializable(typeof(BleReadResult))]
internal partial class BleJsonContext : JsonSerializerContext;
