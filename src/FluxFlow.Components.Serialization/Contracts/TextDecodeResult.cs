namespace FluxFlow.Components.Serialization.Contracts;

public sealed record TextDecodeResult
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Text { get; init; } = "";
    public int ByteCount { get; init; }
    public string Encoding { get; init; } = "utf-8";
}
