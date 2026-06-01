namespace FluxFlow.Components.Serialization.Contracts;

public sealed record Base64EncodeResult
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Text { get; init; } = "";
    public int ByteCount { get; init; }
    public int EncodedLength { get; init; }
}
