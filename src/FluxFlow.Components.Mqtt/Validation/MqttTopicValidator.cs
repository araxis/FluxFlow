using System.Text;

namespace FluxFlow.Components.Mqtt.Validation;

public static class MqttTopicValidator
{
    private const int MaxTopicBytes = 65_535;

    public static MqttTopicValidationResult ValidatePublishTopic(string? topic)
    {
        var commonResult = ValidateCommon(topic, "MQTT publish topic is required.");
        if (!commonResult.IsValid)
        {
            return commonResult;
        }

        if (topic!.Contains('#') || topic.Contains('+'))
        {
            return MqttTopicValidationResult.Invalid(
                "MQTT publish topic cannot contain wildcard characters.");
        }

        return MqttTopicValidationResult.Valid;
    }

    public static MqttTopicValidationResult ValidateSubscriptionFilter(string? topicFilter)
    {
        var commonResult = ValidateCommon(topicFilter, "MQTT subscription topic filter is required.");
        if (!commonResult.IsValid)
        {
            return commonResult;
        }

        var levels = topicFilter!.Split('/');
        for (var index = 0; index < levels.Length; index++)
        {
            var level = levels[index];
            if (level.Contains('#') && (level != "#" || index != levels.Length - 1))
            {
                return MqttTopicValidationResult.Invalid(
                    "MQTT multi-level wildcard must occupy the final topic filter level.");
            }

            if (level.Contains('+') && level != "+")
            {
                return MqttTopicValidationResult.Invalid(
                    "MQTT single-level wildcard must occupy a complete topic filter level.");
            }
        }

        return MqttTopicValidationResult.Valid;
    }

    private static MqttTopicValidationResult ValidateCommon(string? value, string requiredMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return MqttTopicValidationResult.Invalid(requiredMessage);
        }

        if (value.Contains('\0'))
        {
            return MqttTopicValidationResult.Invalid(
                "MQTT topic cannot contain the null character.");
        }

        if (Encoding.UTF8.GetByteCount(value) > MaxTopicBytes)
        {
            return MqttTopicValidationResult.Invalid(
                $"MQTT topic cannot exceed {MaxTopicBytes} UTF-8 encoded bytes.");
        }

        return MqttTopicValidationResult.Valid;
    }
}
