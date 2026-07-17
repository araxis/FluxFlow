using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Data;

public static class FlowValueCanonicalJson
{
    public static string Serialize(FlowValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.Serialize(value);
    }

    public static byte[] SerializeToUtf8Bytes(FlowValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.SerializeToUtf8Bytes(value);
    }

    public static FlowValue Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<FlowValue>(json)
            ?? throw new JsonException("Canonical FlowValue JSON cannot be null.");
    }

    public static FlowValue Deserialize(ReadOnlySpan<byte> utf8Json)
        => JsonSerializer.Deserialize<FlowValue>(utf8Json)
            ?? throw new JsonException("Canonical FlowValue JSON cannot be null.");
}

internal sealed class FlowValueJsonConverter : JsonConverter<FlowValue>
{
    private const string KindProperty = "kind";
    private const string ValueProperty = "value";

    public override FlowValue Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return ReadValue(document.RootElement);
    }

    public override void Write(
        Utf8JsonWriter writer,
        FlowValue value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteValue(writer, value);
    }

    private static void WriteValue(Utf8JsonWriter writer, FlowValue value)
    {
        writer.WriteStartObject();
        writer.WriteString(KindProperty, ToName(value.Kind));
        if (value.Kind != FlowValueKind.Null)
        {
            writer.WritePropertyName(ValueProperty);
            WritePayload(writer, value);
        }

        writer.WriteEndObject();
    }

    private static void WritePayload(Utf8JsonWriter writer, FlowValue value)
    {
        switch (value.Kind)
        {
            case FlowValueKind.Boolean:
                writer.WriteBooleanValue(value.GetBoolean());
                break;
            case FlowValueKind.Integer:
                writer.WriteStringValue(value.GetInteger().ToString(CultureInfo.InvariantCulture));
                break;
            case FlowValueKind.Decimal:
                writer.WriteStringValue(value.GetDecimal().ToString("G29", CultureInfo.InvariantCulture));
                break;
            case FlowValueKind.FloatingPoint:
                writer.WriteStringValue(value.GetFloatingPoint().ToString("R", CultureInfo.InvariantCulture));
                break;
            case FlowValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case FlowValueKind.Binary:
                writer.WriteBase64StringValue(value.GetBinary().AsSpan());
                break;
            case FlowValueKind.DateTimeOffset:
                writer.WriteStringValue(value.GetDateTimeOffset().ToString("O", CultureInfo.InvariantCulture));
                break;
            case FlowValueKind.Date:
                writer.WriteStringValue(value.GetDate().ToString("O", CultureInfo.InvariantCulture));
                break;
            case FlowValueKind.Time:
                writer.WriteStringValue(value.GetTime().ToString("O", CultureInfo.InvariantCulture));
                break;
            case FlowValueKind.Duration:
                writer.WriteStringValue(value.GetDuration().ToString("c", CultureInfo.InvariantCulture));
                break;
            case FlowValueKind.Guid:
                writer.WriteStringValue(value.GetGuid().ToString("D"));
                break;
            case FlowValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.GetArray()) WriteValue(writer, item);
                writer.WriteEndArray();
                break;
            case FlowValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.GetObject().OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteValue(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            default:
                throw new JsonException($"Unsupported FlowValue kind '{value.Kind}'.");
        }
    }

    private static FlowValue ReadValue(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException("Canonical FlowValue JSON must be an object.");

        var kindCount = 0;
        var valueCount = 0;
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(KindProperty))
                kindCount++;
            else if (property.NameEquals(ValueProperty))
                valueCount++;
            else
                throw new JsonException($"Canonical FlowValue JSON contains unknown property '{property.Name}'.");
        }

        if (kindCount != 1 || valueCount > 1)
            throw new JsonException("Canonical FlowValue JSON contains duplicate properties.");

        if (!element.TryGetProperty(KindProperty, out var kindElement) ||
            kindElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Canonical FlowValue JSON requires a string 'kind' property.");
        }

        var kindName = kindElement.GetString();
        var kind = ParseName(kindName);
        if (kind == FlowValueKind.Null)
        {
            if (valueCount != 0)
                throw new JsonException("Canonical null FlowValue JSON cannot contain a 'value' property.");
            return FlowValue.Null;
        }

        if (valueCount != 1 || !element.TryGetProperty(ValueProperty, out var valueElement))
            throw new JsonException($"Canonical FlowValue kind '{kindName}' requires a 'value' property.");

        try
        {
            return kind switch
            {
                FlowValueKind.Boolean => FlowValue.From(valueElement.GetBoolean()),
                FlowValueKind.Integer => FlowValue.From(BigInteger.Parse(
                    ReadString(valueElement), CultureInfo.InvariantCulture)),
                FlowValueKind.Decimal => FlowValue.From(decimal.Parse(
                    ReadString(valueElement),
                    NumberStyles.Number | NumberStyles.AllowExponent,
                    CultureInfo.InvariantCulture)),
                FlowValueKind.FloatingPoint => FlowValue.From(double.Parse(
                    ReadString(valueElement),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture)),
                FlowValueKind.String => FlowValue.From(ReadString(valueElement)),
                FlowValueKind.Binary => FlowValue.FromBinary(valueElement.GetBytesFromBase64()),
                FlowValueKind.DateTimeOffset => FlowValue.From(DateTimeOffset.ParseExact(
                    ReadString(valueElement), "O", CultureInfo.InvariantCulture)),
                FlowValueKind.Date => FlowValue.From(DateOnly.ParseExact(
                    ReadString(valueElement), "O", CultureInfo.InvariantCulture)),
                FlowValueKind.Time => FlowValue.From(TimeOnly.ParseExact(
                    ReadString(valueElement), "O", CultureInfo.InvariantCulture)),
                FlowValueKind.Duration => FlowValue.From(TimeSpan.ParseExact(
                    ReadString(valueElement), "c", CultureInfo.InvariantCulture)),
                FlowValueKind.Guid => FlowValue.From(Guid.ParseExact(ReadString(valueElement), "D")),
                FlowValueKind.Array => ReadArray(valueElement),
                FlowValueKind.Object => ReadObject(valueElement),
                _ => throw new JsonException($"Unsupported FlowValue kind '{kindName}'.")
            };
        }
        catch (Exception exception) when (
            exception is FormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            throw new JsonException($"Canonical FlowValue kind '{kindName}' has an invalid value.", exception);
        }
    }

    private static FlowValue ReadArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new JsonException("Canonical FlowValue array payload must be an array.");

        return FlowValue.FromArray(element.EnumerateArray().Select(ReadValue));
    }

    private static FlowValue ReadObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException("Canonical FlowValue object payload must be an object.");

        return FlowValue.FromObject(element.EnumerateObject().Select(
            property => new KeyValuePair<string, FlowValue>(property.Name, ReadValue(property.Value))));
    }

    private static string ReadString(JsonElement element)
        => element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? throw new JsonException("FlowValue string payload cannot be null.")
            : throw new JsonException("FlowValue payload must be a string.");

    private static string ToName(FlowValueKind kind)
        => kind switch
        {
            FlowValueKind.Null => "null",
            FlowValueKind.Boolean => "boolean",
            FlowValueKind.Integer => "integer",
            FlowValueKind.Decimal => "decimal",
            FlowValueKind.FloatingPoint => "floatingPoint",
            FlowValueKind.String => "string",
            FlowValueKind.Binary => "binary",
            FlowValueKind.DateTimeOffset => "dateTimeOffset",
            FlowValueKind.Date => "date",
            FlowValueKind.Time => "time",
            FlowValueKind.Duration => "duration",
            FlowValueKind.Guid => "guid",
            FlowValueKind.Array => "array",
            FlowValueKind.Object => "object",
            _ => throw new JsonException($"Unsupported FlowValue kind '{kind}'.")
        };

    private static FlowValueKind ParseName(string? value)
        => value switch
        {
            "null" => FlowValueKind.Null,
            "boolean" => FlowValueKind.Boolean,
            "integer" => FlowValueKind.Integer,
            "decimal" => FlowValueKind.Decimal,
            "floatingPoint" => FlowValueKind.FloatingPoint,
            "string" => FlowValueKind.String,
            "binary" => FlowValueKind.Binary,
            "dateTimeOffset" => FlowValueKind.DateTimeOffset,
            "date" => FlowValueKind.Date,
            "time" => FlowValueKind.Time,
            "duration" => FlowValueKind.Duration,
            "guid" => FlowValueKind.Guid,
            "array" => FlowValueKind.Array,
            "object" => FlowValueKind.Object,
            _ => throw new JsonException($"Unknown FlowValue kind '{value}'.")
        };
}
