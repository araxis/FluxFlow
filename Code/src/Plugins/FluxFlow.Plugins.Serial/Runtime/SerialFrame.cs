using System.Text.Json.Serialization;

namespace FluxFlow.Components.Serial;

public sealed record SerialFrame(string DataBase64, string? Text, string? DecodingError);
public sealed record SerialBytesFrame(IReadOnlyList<byte> Bytes, string? Text, string? DecodingError);
public sealed record SerialFrameWrite
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<byte>? Bytes { get; init; }
    public string? DataBase64 { get; init; }
    public string? Text { get; init; }
}

[JsonSerializable(typeof(SerialFrame))]
[JsonSerializable(typeof(SerialBytesFrame))]
[JsonSerializable(typeof(SerialFrameWrite))]
internal sealed partial class SerialFrameJsonContext : JsonSerializerContext;
