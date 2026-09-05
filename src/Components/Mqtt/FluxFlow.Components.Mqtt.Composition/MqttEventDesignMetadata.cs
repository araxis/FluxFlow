using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Mqtt.Composition;

internal static class MqttEventDesignMetadata
{
    internal static IReadOnlyList<EventDesignMetadata> Command { get; } =
    [
        Event("mqtt.command.rejected", "attributes.client", "attributes.errorCode"),
        Event("mqtt.command.completed", "attributes.client", "attributes.operation", "attributes.kind"),
        Event("mqtt.command.failed", "attributes.client", "attributes.operation", "attributes.errorCode")
    ];

    internal static IReadOnlyList<EventDesignMetadata> Publish { get; } =
    [
        Event("mqtt.publish.rejected", "attributes.client", "attributes.errorCode"),
        Event("mqtt.publish.completed", "attributes.client", "attributes.topic", "attributes.qos", "attributes.retain"),
        Event("mqtt.publish.failed", "attributes.client", "attributes.topic", "attributes.qos", "attributes.retain", "attributes.errorCode")
    ];

    internal static IReadOnlyList<EventDesignMetadata> Receive { get; } =
    [
        Event("mqtt.receive.received", "attributes.client", "attributes.trigger", "attributes.topic", "attributes.traceId"),
        Event("mqtt.receive.outcome", "attributes.client", "attributes.trigger", "attributes.traceId", "attributes.outcome"),
        Event("mqtt.receive.outcome-ignored", "attributes.client", "attributes.trigger", "attributes.traceId", "attributes.outcome")
    ];

    private static EventDesignMetadata Event(string type, params string[] dimensions)
        => new()
        {
            Type = type,
            Fields = dimensions.Select(static path => new EventFieldDesignMetadata
            {
                Path = path,
                Role = EventFieldRole.Dimension,
                ValueKind = path.EndsWith("retain", StringComparison.Ordinal)
                    ? EventFieldValueKind.Boolean
                    : EventFieldValueKind.String
            }).ToArray()
        };
}
