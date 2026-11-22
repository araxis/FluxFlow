using FluxFlow.Core.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Core.Messaging;

public sealed class FlowMessageJsonConverter : JsonConverter<FlowMessage>
{
    public override FlowMessage Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("FlowMessage JSON must be an object.");

        EnsureKnownUniqueProperties(root);
        var isError = ReadRequired<bool>(root, nameof(FlowMessage.IsError), options);
        var traceId = ReadRequired<TraceId>(root, nameof(FlowMessage.TraceId), options);
        var messageId = ReadRequired<MessageId>(root, nameof(FlowMessage.MessageId), options);
        var timestamp = ReadRequired<DateTimeOffset>(root, nameof(FlowMessage.Timestamp), options);
        var causationId = ReadOptional<MessageId?>(root, nameof(FlowMessage.CausationId), options);
        var correlationId = ReadOptional<CorrelationId?>(root, nameof(FlowMessage.CorrelationId), options);
        var headers = ReadOptional<Dictionary<string, FlowValue>>(root, nameof(FlowMessage.Headers), options);

        if (!root.TryGetProperty(nameof(FlowMessage.Value), out var valueElement))
            throw new JsonException("FlowMessage JSON requires a 'Value' property.");
        if (!root.TryGetProperty(nameof(FlowMessage.Error), out var errorElement))
            throw new JsonException("FlowMessage JSON requires an 'Error' property.");

        if (isError)
        {
            if (valueElement.ValueKind != JsonValueKind.Null)
                throw new JsonException("An error FlowMessage must contain a null value.");
            if (errorElement.ValueKind == JsonValueKind.Null)
                throw new JsonException("An error FlowMessage requires an error.");

            var error = errorElement.Deserialize<FlowError>(options)
                ?? throw new JsonException("An error FlowMessage requires an error.");
            return new FlowMessage(error, messageId, traceId, timestamp, correlationId, causationId, headers);
        }

        if (errorElement.ValueKind != JsonValueKind.Null)
            throw new JsonException("A value FlowMessage must contain a null error.");

        return new FlowMessage(new JsonFlowValue(valueElement), messageId, traceId, timestamp,
            correlationId, causationId, headers);
    }

    public override void Write(
        Utf8JsonWriter writer,
        FlowMessage message,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName(nameof(FlowMessage.TraceId));
        JsonSerializer.Serialize(writer, message.TraceId, options);
        writer.WritePropertyName(nameof(FlowMessage.MessageId));
        JsonSerializer.Serialize(writer, message.MessageId, options);
        writer.WritePropertyName(nameof(FlowMessage.CausationId));
        JsonSerializer.Serialize(writer, message.CausationId, options);
        writer.WritePropertyName(nameof(FlowMessage.CorrelationId));
        JsonSerializer.Serialize(writer, message.CorrelationId, options);
        writer.WriteString(nameof(FlowMessage.Timestamp), message.Timestamp);
        writer.WritePropertyName(nameof(FlowMessage.Headers));
        JsonSerializer.Serialize(writer, message.Headers, options);
        writer.WriteBoolean(nameof(FlowMessage.IsError), message.IsError);
        writer.WritePropertyName(nameof(FlowMessage.Value));
        if (message.IsError)
            writer.WriteNullValue();
        else
            JsonSerializer.Serialize(writer, message.Value, options);
        writer.WritePropertyName(nameof(FlowMessage.Error));
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

    private static TValue? ReadOptional<TValue>(
        JsonElement root,
        string name,
        JsonSerializerOptions options)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return default;
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
                nameof(FlowMessage.TraceId) or nameof(FlowMessage.MessageId) or nameof(FlowMessage.CausationId) or nameof(FlowMessage.CorrelationId) or
                nameof(FlowMessage.Timestamp) or nameof(FlowMessage.Headers) or nameof(FlowMessage.IsError) or nameof(FlowMessage.Value) or nameof(FlowMessage.Error)))
            {
                throw new JsonException($"FlowMessage JSON contains unknown property '{property.Name}'.");
            }
        }
    }
}

public sealed class FlowMessageJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType &&
           typeToConvert.GetGenericTypeDefinition() == typeof(FlowMessage<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(
            typeof(FlowMessageJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

/// <summary>Converts a typed envelope. Register a closed instance in JsonSerializerOptions
/// to avoid runtime construction through FlowMessageJsonConverterFactory.</summary>
public sealed class FlowMessageJsonConverter<T> : JsonConverter<FlowMessage<T>>
    where T : FlowValue
{
    private static readonly FlowMessageJsonConverter Envelope = new();

    public override FlowMessage<T> Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        var message = Envelope.Read(ref reader, typeof(FlowMessage), options);
        if (message.IsError) return new FlowMessage<T>(message);
        var value = message.Value is T typed ? typed : message.Value.Json.Deserialize<T>(options);
        if (value is null) throw new JsonException("A value message requires a FlowValue.");
        return new FlowMessage<T>(message, value);
    }

    public override void Write(Utf8JsonWriter writer, FlowMessage<T> message,
        JsonSerializerOptions options) => Envelope.Write(writer, message, options);
}
