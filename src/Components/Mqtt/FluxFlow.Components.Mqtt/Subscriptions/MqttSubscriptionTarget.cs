using System.Text.Json.Serialization;

namespace FluxFlow.Components.Mqtt.Subscriptions;

[JsonConverter(typeof(MqttSubscriptionTargetJsonConverter))]
public sealed record MqttSubscriptionTarget
{
    private MqttSubscriptionTarget(
        string identity,
        string? name,
        MqttSubscriptionDefinition? inline)
    {
        Identity = identity;
        Name = name;
        Inline = inline;
    }

    public string Identity { get; }

    public string? Name { get; }

    public MqttSubscriptionDefinition? Inline { get; }

    public bool IsNamed => Name is not null;

    public static MqttSubscriptionTarget Named(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim();
        return new($"name:{normalized}", normalized, inline: null);
    }

    public static MqttSubscriptionTarget FromInline(MqttSubscriptionDefinition subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription.TopicFilter);
        var filter = subscription.TopicFilter.Trim();
        return new(
            $"filter:{filter}",
            name: null,
            subscription with { TopicFilter = filter });
    }
}
