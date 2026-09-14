using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Nodes;

[JsonConverter(typeof(TraceIdJsonConverter))]
public readonly record struct TraceId
{
    private readonly string _value;

    public TraceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value;
    }

    public string Value => _value
        ?? throw new InvalidOperationException("TraceId was not initialized.");

    public bool IsEmpty => string.IsNullOrEmpty(_value);

    public static TraceId New() => new(Guid.NewGuid().ToString("n"));

    public override string ToString() => _value ?? string.Empty;
}

internal sealed class TraceIdJsonConverter : JsonConverter<TraceId>
{
    public override TraceId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => new(reader.GetString() ?? throw new JsonException("TraceId cannot be null."));

    public override void Write(
        Utf8JsonWriter writer,
        TraceId value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
