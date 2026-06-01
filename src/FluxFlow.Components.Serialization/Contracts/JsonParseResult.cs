using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluxFlow.Components.Serialization.Contracts;

public sealed record JsonParseResult
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public JsonNode? Value { get; init; }
    public JsonValueKind Kind { get; init; }
    public string Text { get; init; } = "";
    public int ByteCount { get; init; }
    public string Encoding { get; init; } = "utf-8";
}
