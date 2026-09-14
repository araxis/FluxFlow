using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Data;

namespace FluxFlow.Components.Mqtt.Nodes;

/// <summary>Owns the structural contract accepted by the MQTT publish component.</summary>
internal static class MqttPublishJson
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new PublishContentConverter() } };


    private static readonly MqttPublishJsonContext JsonContext = new(SerializerOptions);

    internal static MqttPublishMessage Read(FlowValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is MqttPublishMessage typed) return typed;
        return value.Json.Deserialize(JsonContext.MqttPublishMessage)
            ?? throw new JsonException("MqttPublishMessage cannot be null.");
    }

    // Only this component accepts text or arbitrary JSON as publish content.
    private sealed class PublishContentConverter : JsonConverter<FlowContent>
    {
        public override FlowContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return FlowContent.FromBytes(Encoding.UTF8.GetBytes(reader.GetString()!), "text/plain", "utf-8");

            using var document = JsonDocument.ParseValue(ref reader);
            var content = document.RootElement;
            if (content.ValueKind == JsonValueKind.Object && content.EnumerateObject().Any(
                property => string.Equals(property.Name, "bytes", StringComparison.OrdinalIgnoreCase)))
                return content.Deserialize<FlowContent>()!;

            return FlowContent.FromBytes(JsonSerializer.SerializeToUtf8Bytes(content), "application/json", "utf-8");
        }

        public override void Write(Utf8JsonWriter writer, FlowContent value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value);
    }
}
