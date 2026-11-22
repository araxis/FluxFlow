using System.Text.Json.Serialization;

namespace FluxFlow.Components.Serial;

public sealed record SerialWriteRequest
{
    public required string Text { get; init; }
}

[JsonSerializable(typeof(SerialWriteRequest))]
internal sealed partial class SerialJsonContext : JsonSerializerContext;
