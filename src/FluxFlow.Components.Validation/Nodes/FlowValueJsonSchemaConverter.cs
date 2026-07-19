using System.Globalization;
using System.Text.Json;
using FluxFlow.Data;

namespace FluxFlow.Components.Validation.Nodes;

internal static class FlowValueJsonSchemaConverter
{
    internal static JsonElement Convert(FlowValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(writer, value);
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void Write(Utf8JsonWriter writer, FlowValue value)
    {
        switch (value.Kind)
        {
            case FlowValueKind.Null:
                writer.WriteNullValue();
                break;
            case FlowValueKind.Boolean:
                writer.WriteBooleanValue(value.GetBoolean());
                break;
            case FlowValueKind.Integer:
                writer.WriteRawValue(
                    value.GetInteger().ToString(CultureInfo.InvariantCulture),
                    skipInputValidation: false);
                break;
            case FlowValueKind.Decimal:
                writer.WriteNumberValue(value.GetDecimal());
                break;
            case FlowValueKind.FloatingPoint:
                writer.WriteNumberValue(value.GetFloatingPoint());
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
                foreach (var item in value.GetArray())
                    Write(writer, item);
                writer.WriteEndArray();
                break;
            case FlowValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.GetObject().OrderBy(
                             item => item.Key,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            default:
                throw new InvalidOperationException(
                    $"FlowValue kind '{value.Kind}' cannot be evaluated by JSON Schema.");
        }
    }
}
