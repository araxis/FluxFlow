using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Data;

internal sealed class FlowContentJsonConverter : JsonConverter<FlowContent>
{
    private const int CurrentFormatVersion = 1;

    public override FlowContent Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Flow content must be a JSON object.");

        var versionElement = GetRequiredProperty(root, "formatVersion");
        if (!versionElement.TryGetInt32(out var version))
            throw new JsonException("Flow content formatVersion must be an integer.");
        if (version != CurrentFormatVersion)
        {
            throw new JsonException(
                $"Flow content format version '{version}' is not supported.");
        }

        var bytesElement = GetRequiredProperty(root, "bytes");
        if (bytesElement.ValueKind != JsonValueKind.String)
            throw new JsonException("Flow content bytes must be a Base64 string.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(bytesElement.GetString()!);
        }
        catch (FormatException exception)
        {
            throw new JsonException("Flow content bytes are not valid Base64.", exception);
        }

        return FlowContent.FromBytes(
            bytes,
            GetOptionalString(root, "contentType"),
            GetOptionalString(root, "encoding"));
    }

    public override void Write(
        Utf8JsonWriter writer,
        FlowContent value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", CurrentFormatVersion);
        writer.WriteBase64String("bytes", value.Bytes.AsSpan());
        WriteOptionalString(writer, "contentType", value.ContentType);
        WriteOptionalString(writer, "encoding", value.Encoding);
        writer.WriteEndObject();
    }

    private static JsonElement GetRequiredProperty(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(property.Name, name))
                return property.Value;
        }

        throw new JsonException($"Flow content is missing '{name}'.");
    }

    private static string? GetOptionalString(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(property.Name, name))
                continue;
            if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return null;
            if (property.Value.ValueKind != JsonValueKind.String)
                throw new JsonException($"Flow content {name} must be a string or null.");
            return property.Value.GetString();
        }

        return null;
    }

    private static void WriteOptionalString(
        Utf8JsonWriter writer,
        string name,
        string? value)
    {
        if (value is null)
            writer.WriteNull(name);
        else
            writer.WriteString(name, value);
    }
}
