using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Data;

namespace FluxFlow.Nodes;

internal sealed class FlowMessageJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType &&
           typeToConvert.GetGenericTypeDefinition() == typeof(FlowMessage<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(
            typeof(FlowMessageJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

internal sealed class FlowMessageJsonConverter<T> : JsonConverter<FlowMessage<T>>
{
    public override FlowMessage<T> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("FlowMessage JSON must be an object.");

        EnsureKnownUniqueProperties(root);
        var isError = ReadRequired<bool>(root, "isError", options);
        var traceId = ReadRequired<TraceId>(root, "traceId", options);
        var messageId = ReadRequired<MessageId>(root, "messageId", options);
        var timestamp = ReadRequired<DateTimeOffset>(root, "timestamp", options);
        var causationId = ReadOptionalStruct<MessageId>(root, "causationId", options);
        var correlationId = ReadOptionalStruct<CorrelationId>(root, "correlationId", options);
        var headers = ReadOptionalClass<Dictionary<string, string>>(root, "headers", options);

        if (!root.TryGetProperty("value", out var valueElement))
            throw new JsonException("FlowMessage JSON requires a 'value' property.");
        if (!root.TryGetProperty("error", out var errorElement))
            throw new JsonException("FlowMessage JSON requires an 'error' property.");

        if (isError)
        {
            if (valueElement.ValueKind != JsonValueKind.Null)
                throw new JsonException("An error FlowMessage must contain a null value.");
            if (errorElement.ValueKind == JsonValueKind.Null)
                throw new JsonException("An error FlowMessage requires an error.");

            var error = errorElement.Deserialize<FlowError>(options)
                ?? throw new JsonException("An error FlowMessage requires an error.");
            return FlowMessage<T>.Rehydrate(
                true,
                value: default,
                error,
                correlationId,
                traceId,
                messageId,
                causationId,
                timestamp,
                headers);
        }

        if (errorElement.ValueKind != JsonValueKind.Null)
            throw new JsonException("A value FlowMessage must contain a null error.");

        var value = valueElement.Deserialize<T>(options);
        return FlowMessage<T>.Rehydrate(
            false,
            value,
            error: null,
            correlationId,
            traceId,
            messageId,
            causationId,
            timestamp,
            headers);
    }

    public override void Write(
        Utf8JsonWriter writer,
        FlowMessage<T> message,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("traceId");
        JsonSerializer.Serialize(writer, message.TraceId, options);
        writer.WritePropertyName("messageId");
        JsonSerializer.Serialize(writer, message.MessageId, options);
        writer.WritePropertyName("causationId");
        JsonSerializer.Serialize(writer, message.CausationId, options);
        writer.WritePropertyName("correlationId");
        JsonSerializer.Serialize(writer, message.CorrelationId, options);
        writer.WriteString("timestamp", message.Timestamp);
        writer.WritePropertyName("headers");
        JsonSerializer.Serialize(writer, message.Headers, options);
        writer.WriteBoolean("isError", message.IsError);
        writer.WritePropertyName("value");
        if (message.IsError)
            writer.WriteNullValue();
        else
            JsonSerializer.Serialize(writer, message.Value, options);
        writer.WritePropertyName("error");
        if (message.IsError)
            JsonSerializer.Serialize(writer, message.Error, options);
        else
            writer.WriteNullValue();
        writer.WriteEndObject();
    }

    private static TValue ReadRequired<TValue>(
        JsonElement root,
        string name,
        JsonSerializerOptions options)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            throw new JsonException($"FlowMessage JSON requires a '{name}' property.");
        return element.Deserialize<TValue>(options)
            ?? throw new JsonException($"FlowMessage JSON property '{name}' is invalid.");
    }

    private static TValue? ReadOptionalStruct<TValue>(
        JsonElement root,
        string name,
        JsonSerializerOptions options)
        where TValue : struct
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return null;
        return element.Deserialize<TValue>(options);
    }

    private static TValue? ReadOptionalClass<TValue>(
        JsonElement root,
        string name,
        JsonSerializerOptions options)
        where TValue : class
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return null;
        return element.Deserialize<TValue>(options);
    }

    private static void EnsureKnownUniqueProperties(JsonElement root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw new JsonException($"FlowMessage JSON contains duplicate property '{property.Name}'.");
            if (property.Name is not (
                "traceId" or "messageId" or "causationId" or "correlationId" or
                "timestamp" or "headers" or "isError" or "value" or "error"))
            {
                throw new JsonException($"FlowMessage JSON contains unknown property '{property.Name}'.");
            }
        }
    }
}
