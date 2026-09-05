using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace FluxFlow.Data;

/// <summary>
/// Canonical structured value exchanged by runtime-authored workflows.
/// Domain components materialize this value into their own internal request types.
/// This is the universal runtime payload, not the message envelope and not an
/// exact-content abstraction; use <see cref="FlowContent"/> when byte identity matters.
/// </summary>
[JsonConverter(typeof(FlowValueJsonConverter))]
public sealed class FlowValue
{
    private static readonly JsonSerializerOptions DefaultSerializerOptions =
        CreateDefaultSerializerOptions();
    private readonly JsonElement _value;

    private FlowValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("A flow value cannot be undefined.", nameof(value));

        _value = value.Clone();
    }

    public JsonValueKind Kind => _value.ValueKind;

    /// <summary>An explicit JSON null value.</summary>
    public static FlowValue Null { get; } =
        FromJson(JsonSerializer.SerializeToElement<object?>(null));

    public static FlowValue FromJson(JsonElement value) => new(value);

    public static FlowValue From<T>(T value, JsonSerializerOptions? options = null)
    {
        if (value is FlowValue flowValue)
            return flowValue;
        if (value is JsonElement element)
            return FromJson(element);
        if (value is JsonDocument document)
            return FromJson(document.RootElement);

        var serializerOptions = options ?? DefaultSerializerOptions;
        return value is null
            ? Null
            : new FlowValue(JsonSerializer.SerializeToElement(
                value,
                value.GetType(),
                serializerOptions));
    }

    public JsonElement ToJsonElement() => _value.Clone();

    public T? Deserialize<T>(JsonSerializerOptions? options = null)
        => _value.Deserialize<T>(options ?? DefaultSerializerOptions);

    internal void WriteTo(Utf8JsonWriter writer) => _value.WriteTo(writer);

    public override string ToString() => _value.GetRawText();

    private static JsonSerializerOptions CreateDefaultSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(static typeInfo =>
        {
            foreach (var property in typeInfo.Properties)
            {
                if (property.PropertyType == typeof(JsonElement))
                {
                    property.ShouldSerialize = static (_, value) =>
                        value is JsonElement element &&
                        element.ValueKind != JsonValueKind.Undefined;
                }
            }
        });
        options.TypeInfoResolver = resolver;
        options.Converters.Add(new UndefinedJsonElementConverter());
        return options;
    }

    private sealed class UndefinedJsonElementConverter : JsonConverter<JsonElement>
    {
        public override JsonElement Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.Clone();
        }

        public override void Write(
            Utf8JsonWriter writer,
            JsonElement value,
            JsonSerializerOptions options)
        {
            if (value.ValueKind == JsonValueKind.Undefined)
                writer.WriteNullValue();
            else
                value.WriteTo(writer);
        }
    }
}
