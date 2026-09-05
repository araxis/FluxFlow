using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Data;

internal sealed class FlowValueJsonConverter : JsonConverter<FlowValue>
{
    public override FlowValue Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return FlowValue.FromJson(document.RootElement);
    }

    public override void Write(
        Utf8JsonWriter writer,
        FlowValue value,
        JsonSerializerOptions options)
        => value.WriteTo(writer);
}
