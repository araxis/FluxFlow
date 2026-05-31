using FluxFlow.Components.Mqtt.Validation;
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
        EnsureValidPublishTopic(options.DefaultTopic, nameof(options.DefaultTopic), required: false);
        return options;
    }

    public static MqttSubscriptionOptions ReadSubscriptionOptions(NodeDefinition definition)
    {
        var options = Read<MqttSubscriptionOptions>(definition);
        EnsurePositive(options.BoundedCapacity, nameof(options.BoundedCapacity));
        EnsureDefined(options.QualityOfService, nameof(options.QualityOfService));

        EnsureValidSubscriptionFilter(options.TopicFilter, nameof(options.TopicFilter));

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

    private static void EnsureValidPublishTopic(string? value, string name, bool required)
    {
        if (!required && value is null)
        {
            return;
        }

        var result = MqttTopicValidator.ValidatePublishTopic(value);
        if (!result.IsValid)
        {
            throw new InvalidOperationException($"MQTT option '{name}' is invalid: {result.Message}");
        }
    }

    private static void EnsureValidSubscriptionFilter(string? value, string name)
    {
        var result = MqttTopicValidator.ValidateSubscriptionFilter(value);
        if (!result.IsValid)
        {
            throw new InvalidOperationException($"MQTT option '{name}' is invalid: {result.Message}");
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
