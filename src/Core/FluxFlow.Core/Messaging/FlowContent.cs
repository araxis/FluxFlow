using System.Text;
using System.Text.Json;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace FluxFlow.Data;

/// <summary>
/// Exact immutable bytes with optional transport content metadata.
/// This type represents opaque text or binary content; it is not the universal value
/// exchanged by runtime-authored workflows. Such workflows carry <see cref="FlowValue"/>,
/// which may structurally contain or materialize into content when a component needs it.
/// </summary>
[JsonConverter(typeof(FlowContentJsonConverter))]
public sealed record FlowContent
{
    [JsonConstructor]
    public FlowContent(
        ImmutableArray<byte> bytes,
        string? contentType = null,
        string? encoding = null)
    {
        Bytes = bytes.IsDefault ? ImmutableArray<byte>.Empty : bytes;
        ContentType = NormalizeOptional(contentType);
        Encoding = NormalizeOptional(encoding);
    }

    public ImmutableArray<byte> Bytes { get; }

    public string? ContentType { get; }

    public string? Encoding { get; }

    public static FlowContent FromBytes(
        ReadOnlyMemory<byte> bytes,
        string? contentType = null,
        string? encoding = null)
        => new(ImmutableArray.CreateRange(bytes.ToArray()), contentType, encoding);

    /// <summary>Read exact content, plain text, or JSON using the portable content convention.</summary>
    public static FlowContent FromJson(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("bytes", out _))
            return json.Deserialize<FlowContent>() ?? throw new JsonException("Flow content cannot be null.");
        return json.ValueKind == JsonValueKind.String
            ? FromBytes(System.Text.Encoding.UTF8.GetBytes(json.GetString() ?? string.Empty), "text/plain", "utf-8")
            : FromBytes(System.Text.Encoding.UTF8.GetBytes(json.GetRawText()), "application/json", "utf-8");
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
