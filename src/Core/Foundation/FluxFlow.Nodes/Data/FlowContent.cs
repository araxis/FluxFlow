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

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
