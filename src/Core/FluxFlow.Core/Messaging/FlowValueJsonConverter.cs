using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Data;

/// <summary>Preserves the JSON value shape for runtime and source-generated serialization.</summary>
public sealed class FlowValueJsonConverter : JsonConverter<FlowValue>
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert == typeof(FlowValue) || typeToConvert == typeof(JsonFlowValue);

    public override bool HandleNull => true;

    public override FlowValue Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return JsonFlowValue.Null;
        using var document = JsonDocument.ParseValue(ref reader);
        return ((FlowValue)new JsonFlowValue(document.RootElement));
    }

    public override void Write(
        Utf8JsonWriter writer,
        FlowValue value,
        JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            value.Json.WriteTo(writer);
    }
}
