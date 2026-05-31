using FluxFlow.Engine.Definitions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Components.Mqtt.Options;

internal static class MqttOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static MqttPublishOptions ReadPublishOptions(NodeDefinition definition)
    {
        var options = Read<MqttPublishOptions>(definition);
        EnsurePositive(options.BoundedCapacity, nameof(options.BoundedCapacity));
        EnsureDefined(options.QualityOfService, nameof(options.QualityOfService));
        return options;
    }

    public static MqttSubscriptionOptions ReadSubscriptionOptions(NodeDefinition definition)
    {
        var options = Read<MqttSubscriptionOptions>(definition);
        EnsurePositive(options.BoundedCapacity, nameof(options.BoundedCapacity));
        EnsureDefined(options.QualityOfService, nameof(options.QualityOfService));

        if (string.IsNullOrWhiteSpace(options.TopicFilter))
        {
            throw new InvalidOperationException("MQTT subscribe node requires a topic filter.");
        }

        return options;
    }

    private static T Read<T>(NodeDefinition definition)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Configuration.Count == 0)
        {
            return new T();
        }

        var json = JsonSerializer.Serialize(definition.Configuration, SerializerOptions);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? new T();
    }

    private static void EnsurePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"MQTT option '{name}' must be greater than zero.");
        }
    }

    private static void EnsureDefined<TEnum>(TEnum value, string name)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new InvalidOperationException($"MQTT option '{name}' is not supported.");
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
