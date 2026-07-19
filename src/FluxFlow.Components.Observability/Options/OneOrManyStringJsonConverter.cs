using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Components.Observability.Options;

internal sealed class OneOrManyStringJsonConverter : JsonConverter<string[]>
{
    public override string[] Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return [];
        if (reader.TokenType == JsonTokenType.String)
            return [reader.GetString() ?? string.Empty];
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected one string or an array of strings.");

        var values = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("Expected an array containing only strings.");
            values.Add(reader.GetString() ?? string.Empty);
        }

        if (reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException("The string array was not terminated.");
        return [.. values];
    }

    public override void Write(
        Utf8JsonWriter writer,
        string[] value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value.Length == 1)
        {
            writer.WriteStringValue(value[0]);
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}
