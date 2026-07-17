using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Nodes;

[JsonConverter(typeof(MessageIdJsonConverter))]
public readonly record struct MessageId
{
    private readonly string _value;

    public MessageId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value;
    }

    public string Value => _value
        ?? throw new InvalidOperationException("MessageId was not initialized.");

    public bool IsEmpty => string.IsNullOrEmpty(_value);

    public static MessageId New() => new(Guid.NewGuid().ToString("n"));

    public override string ToString() => _value ?? string.Empty;
}

internal sealed class MessageIdJsonConverter : JsonConverter<MessageId>
{
    public override MessageId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => new(reader.GetString() ?? throw new JsonException("MessageId cannot be null."));

    public override void Write(
        Utf8JsonWriter writer,
        MessageId value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
