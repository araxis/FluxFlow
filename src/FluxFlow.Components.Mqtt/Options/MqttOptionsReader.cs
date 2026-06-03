using FluxFlow.Components.Mqtt.Contracts;
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
        EnsureValidReconnectPolicy(options.Reconnect);
        return options;
    }

    public static MqttSubscriptionOptions ReadSubscriptionOptions(NodeDefinition definition)
    {
        var options = Read<MqttSubscriptionOptions>(definition);
        EnsurePositive(options.BoundedCapacity, nameof(options.BoundedCapacity));
        EnsureDefined(options.QualityOfService, nameof(options.QualityOfService));

        EnsureValidSubscriptionFilter(options.TopicFilter, nameof(options.TopicFilter));
        EnsureValidReconnectPolicy(options.Reconnect);

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

    private static void EnsureValidReconnectPolicy(MqttReconnectPolicy? policy)
    {
        if (policy is null)
        {
            return;
        }

        if (policy.MaxAttempts.HasValue && policy.MaxAttempts.Value < 0)
        {
            throw new InvalidOperationException(
                "MQTT option 'reconnect.maxAttempts' must be zero or greater when set.");
        }

        if (policy.InitialDelayMilliseconds.HasValue && policy.InitialDelayMilliseconds.Value < 0)
        {
            throw new InvalidOperationException(
                "MQTT option 'reconnect.initialDelayMilliseconds' must be zero or greater when set.");
        }

        if (policy.MaxDelayMilliseconds.HasValue && policy.MaxDelayMilliseconds.Value < 0)
        {
            throw new InvalidOperationException(
                "MQTT option 'reconnect.maxDelayMilliseconds' must be zero or greater when set.");
        }

        if (policy.InitialDelayMilliseconds.HasValue &&
            policy.MaxDelayMilliseconds.HasValue &&
            policy.InitialDelayMilliseconds.Value > policy.MaxDelayMilliseconds.Value)
        {
            throw new InvalidOperationException(
                "MQTT option 'reconnect.initialDelayMilliseconds' cannot be greater than reconnect.maxDelayMilliseconds.");
        }

        if (policy.BackoffMultiplier.HasValue && policy.BackoffMultiplier.Value <= 0)
        {
            throw new InvalidOperationException(
                "MQTT option 'reconnect.backoffMultiplier' must be greater than zero when set.");
        }

        if (policy.Attributes is null)
        {
            return;
        }

        foreach (var (key, _) in policy.Attributes)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    "MQTT option 'reconnect.attributes' cannot contain an empty key.");
            }
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
