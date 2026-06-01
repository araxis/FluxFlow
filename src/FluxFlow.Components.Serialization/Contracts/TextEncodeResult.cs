namespace FluxFlow.Components.Serialization.Contracts;

public sealed record TextEncodeResult
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public byte[] Bytes { get; init; } = [];
    public int ByteCount { get; init; }
    public string Encoding { get; init; } = "utf-8";
}
