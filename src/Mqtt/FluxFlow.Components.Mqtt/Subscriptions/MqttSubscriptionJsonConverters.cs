using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Components.Mqtt.Subscriptions;

internal sealed class MqttSubscriptionTargetJsonConverter : JsonConverter<MqttSubscriptionTarget>
{
    public override MqttSubscriptionTarget Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return MqttSubscriptionTarget.Named(reader.GetString()!);
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("An MQTT subscription must be a named string or inline object.");

        var subscription = JsonSerializer.Deserialize<MqttSubscriptionDefinition>(
            ref reader,
            options) ?? throw new JsonException("The inline MQTT subscription is null.");
        return MqttSubscriptionTarget.FromInline(subscription);
    }

    public override void Write(
        Utf8JsonWriter writer,
        MqttSubscriptionTarget value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Name is not null)
        {
            writer.WriteStringValue(value.Name);
            return;
        }

        JsonSerializer.Serialize(writer, value.Inline, options);
    }
}

internal sealed class MqttSubscriptionListJsonConverter :
    JsonConverter<IReadOnlyList<MqttSubscriptionTarget>>
{
    public override IReadOnlyList<MqttSubscriptionTarget> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            return
            [
                JsonSerializer.Deserialize<MqttSubscriptionTarget>(ref reader, options)
                    ?? throw new JsonException("The MQTT subscription is null.")
            ];
        }

        var values = new List<MqttSubscriptionTarget>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            values.Add(JsonSerializer.Deserialize<MqttSubscriptionTarget>(ref reader, options)
                ?? throw new JsonException("An MQTT subscription array item is null."));
        }

        return values;
    }

    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<MqttSubscriptionTarget> value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Count == 1)
        {
            JsonSerializer.Serialize(writer, value[0], options);
            return;
        }

        writer.WriteStartArray();
        foreach (var subscription in value)
            JsonSerializer.Serialize(writer, subscription, options);
        writer.WriteEndArray();
    }
}
