namespace FluxFlow.Components.Serialization.Contracts;

public sealed record JsonStringifyResult
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Text { get; init; } = "";
    public byte[] Bytes { get; init; } = [];
    public int ByteCount { get; init; }
    public string Encoding { get; init; } = "utf-8";
}
