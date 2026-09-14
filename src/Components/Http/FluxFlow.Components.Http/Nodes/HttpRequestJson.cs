using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Data;

namespace FluxFlow.Components.Http.Nodes;

/// <summary>Owns the structural contract accepted by the HTTP client component.</summary>
internal static class HttpRequestJson
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new RequestBodyConverter() } };


    private static readonly HttpRequestJsonContext JsonContext = new(SerializerOptions);

    internal static HttpClientRequest Read(FlowValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is HttpClientRequest typed) return typed;
        return value.Json.Deserialize(JsonContext.HttpClientRequest)
            ?? throw new JsonException("HttpClientRequest cannot be null.");
    }

    // This request accepts plain text or arbitrary JSON as well as exact FlowContent.
    private sealed class RequestBodyConverter : JsonConverter<FlowContent>
    {
        public override FlowContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return FlowContent.FromBytes(Encoding.UTF8.GetBytes(reader.GetString()!), "text/plain", "utf-8");

            using var document = JsonDocument.ParseValue(ref reader);
            var body = document.RootElement;
            if (body.ValueKind == JsonValueKind.Object && body.EnumerateObject().Any(
                property => string.Equals(property.Name, "bytes", StringComparison.OrdinalIgnoreCase)))
                return body.Deserialize<FlowContent>()!;

            return FlowContent.FromBytes(JsonSerializer.SerializeToUtf8Bytes(body), "application/json", "utf-8");
        }

        public override void Write(Utf8JsonWriter writer, FlowContent value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value);
    }
}
