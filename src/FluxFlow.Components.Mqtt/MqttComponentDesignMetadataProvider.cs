using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Mqtt;

public sealed class MqttComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        new()
        {
            Type = MqttComponentTypes.Connection,
            DisplayName = "MQTT Connection",
            Category = "MQTT",
            Summary = "Owns the shared MQTT client referenced by publish and subscribe nodes. Connecting and disconnecting are host-driven (ConnectAsync/DisconnectAsync on the resolved handle); there is no in-graph command port.",
            IconKey = "mqtt-connection",
            PreferredNodeName = "mqttConnection",
            SuggestedEditorWidth = 460,
            Options =
            [
                Option("profile.host", OptionValueKind.Text, "Host", "Broker host name or address."),
                Option("profile.port", OptionValueKind.Number, "Port", "Broker TCP port.", defaultValue: 1883),
                Option("profile.clientId", OptionValueKind.Text, "Client id"),
                Option("profile.useTls", OptionValueKind.Boolean, "Use TLS", defaultValue: false),
                Option("reconnect.enabled", OptionValueKind.Boolean, "Reconnect enabled", defaultValue: true),
                Option("reconnect.maxAttempts", OptionValueKind.Number, "Max attempts", "Maximum reconnect attempts.", false, null),
                Option("reconnect.initialDelayMilliseconds", OptionValueKind.Number, "Initial delay ms"),
                Option("reconnect.maxDelayMilliseconds", OptionValueKind.Number, "Max delay ms"),
                Option("reconnect.backoffMultiplier", OptionValueKind.Number, "Backoff multiplier")
            ],
            Ports =
            [
                Port("Errors", PortDirection.Output, "FlowError", false)
            ]
        },
        new()
        {
            Type = MqttComponentTypes.Subscribe,
            DisplayName = "MQTT Subscribe",
            Category = "MQTT",
            Summary = "Subscribes to a configured MQTT topic filter and emits received messages.",
            IconKey = "mqtt-subscribe",
            PreferredNodeName = "mqttSubscribe",
            SuggestedEditorWidth = 460,
            Options =
            [
                Option("connectionName", OptionValueKind.Text, "Connection name", "Name of the mqtt.connection resource to use.", true),
                Option("topicFilter", OptionValueKind.Text, "Topic filter", "MQTT topic filter to subscribe to.", true, "#"),
                QualityOfServiceOption(),
                Option("receiveRetainedMessages", OptionValueKind.Boolean, "Receive retained", defaultValue: true),
                Option("retainAsPublished", OptionValueKind.Boolean, "Retain as published", defaultValue: false),
                CapacityOption()
            ],
            Ports =
            [
                Port(MqttComponentPorts.Output, PortDirection.Output, "MqttReceivedMessage", true)
            ]
        },
        new()
        {
            Type = MqttComponentTypes.Publish,
            DisplayName = "MQTT Publish",
            Category = "MQTT",
            Summary = "Publishes MQTT messages from explicit publish requests.",
            IconKey = "mqtt-publish",
            PreferredNodeName = "mqttPublish",
            SuggestedEditorWidth = 460,
            Options =
            [
                Option("connectionName", OptionValueKind.Text, "Connection name", "Name of the mqtt.connection resource to use.", true),
                Option("defaultTopic", OptionValueKind.Text, "Default topic"),
                QualityOfServiceOption(),
                Option("retain", OptionValueKind.Boolean, "Retain", defaultValue: false),
                PublishTimeoutOption(),
                CapacityOption()
            ],
            Ports =
            [
                Port(MqttComponentPorts.Input, PortDirection.Input, "MqttPublishRequest", true),
                Port(MqttComponentPorts.Result, PortDirection.Output, "MqttPublishResult", true, 1)
            ]
        }
    ];

    private static OptionDesignMetadata Option(
        string name,
        OptionValueKind kind,
        string displayName,
        string? helperText = null,
        bool required = false,
        object? defaultValue = null) => new()
        {
            Name = name,
            Kind = kind,
            DisplayName = displayName,
            HelperText = helperText,
            IsRequired = required,
            DefaultValue = defaultValue
        };

    private static OptionDesignMetadata QualityOfServiceOption() => new()
    {
        Name = "qualityOfService",
        Kind = OptionValueKind.Enum,
        DisplayName = "QoS",
        DefaultValue = "AtMostOnce",
        Choices =
        [
            new() { Value = "AtMostOnce", DisplayName = "At most once" },
            new() { Value = "AtLeastOnce", DisplayName = "At least once" },
            new() { Value = "ExactlyOnce", DisplayName = "Exactly once" }
        ]
    };

    private static OptionDesignMetadata PublishTimeoutOption() => new()
    {
        Name = "publishTimeoutMilliseconds",
        Kind = OptionValueKind.Number,
        DisplayName = "Publish timeout ms",
        DefaultValue = 30000,
        Min = 1
    };

    private static OptionDesignMetadata CapacityOption() => new()
    {
        Name = "boundedCapacity",
        Kind = OptionValueKind.Number,
        DisplayName = "Capacity",
        DefaultValue = 128,
        Min = 1
    };

    private static PortDesignMetadata Port(
        string name,
        PortDirection direction,
        string valueType,
        bool primary,
        int order = 0) => new()
        {
            Name = new PortName(name),
            Direction = direction,
            ValueType = valueType,
            IsPrimary = primary,
            Order = order
        };
}
