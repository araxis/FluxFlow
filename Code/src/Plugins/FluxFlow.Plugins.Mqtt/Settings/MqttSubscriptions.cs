using FluxFlow.Components.Mqtt;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Configuration.Components.Mqtt;

public static class MqttSubscriptions
{
    public static void Validate(MqttAcknowledgementMode mode, int pending, IReadOnlyList<MqttTopic> topics)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pending);
        if (mode == MqttAcknowledgementMode.Manual && topics.Any(topic => topic.QoS != 1))
            throw new ArgumentException("Manual acknowledgement requires QoS 1 subscriptions.");
    }

    public static IReadOnlyList<MqttTopic> ReadTopics(IConfigurationSection section)
    {
        var children = section.GetChildren().ToArray();
        if (section.Value is not null && children.Length != 0)
            throw new ArgumentException($"Invalid MQTT subscriptions at '{section.Path}': a scalar cannot also have child values.");
        if (section.Value is null && children.Length == 0) return Array.Empty<MqttTopic>();
        var items = children.Length > 0 && children.All(child => int.TryParse(child.Key, out _)) ? children : [section];
        var topics = new List<MqttTopic>();
        foreach (var item in items)
        {
            try
            {
                if (item.Value is not null && item.GetChildren().Any()) throw new ArgumentException("A topic cannot be both a scalar and an object.");
                var topic = item.Value is { } text ? new MqttTopic { Topic = text }
                    : item.Get<MqttTopic>(options => options.ErrorOnUnknownConfiguration = true)
                        ?? throw new ArgumentException("Expected a topic string or an object with Topic and QoS.");
                topic.Validate();
                topics.Add(topic);
            }
            catch (Exception error) { throw new ArgumentException($"Invalid MQTT subscription at '{item.Path}': {error.Message}", error); }
        }
        return topics.AsReadOnly();
    }
}
