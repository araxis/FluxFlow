using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Nodes;

/// <summary>
/// A strongly-typed correlation id carried by every <see cref="FlowMessage{T}"/>.
/// It groups all messages of one logical exchange (e.g. an inbound request and the
/// response that flows back), and is the key the request/reply triggers correlate
/// on. Backed by a string so a host can flow an existing trace/request id through;
/// <see cref="New"/> defaults to a GUID. Serializes as a bare JSON string.
/// </summary>
[JsonConverter(typeof(CorrelationIdJsonConverter))]
public readonly record struct CorrelationId
{
    private readonly string _value;

    public CorrelationId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value;
    }

    public string Value => _value
        ?? throw new InvalidOperationException("CorrelationId was not initialized.");

    public bool IsEmpty => string.IsNullOrEmpty(_value);

    public static CorrelationId New() => new(Guid.NewGuid().ToString("n"));

    public override string ToString() => _value ?? string.Empty;
}

public sealed class CorrelationIdJsonConverter : JsonConverter<CorrelationId>
{
    public override CorrelationId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => new(reader.GetString()
            ?? throw new JsonException("CorrelationId cannot be null."));

    public override void Write(
        Utf8JsonWriter writer,
        CorrelationId value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
