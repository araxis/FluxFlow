using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;

namespace FluxFlow.Components.Mqtt.Composition;

public sealed class MqttComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    private static readonly MqttPublishOptions PublishDefaults = new();
    private static readonly MqttTriggerOptions TriggerDefaults = new();

    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        =>
        [
            CreatePublishMetadata(),
            CreateTriggerMetadata()
        ];

    private static ComponentDesignMetadata CreatePublishMetadata() => new()
    {
        Type = new ComponentType(MqttCompositionNodeTypes.Publish),
        DisplayName = "MQTT Publish",
        Category = "MQTT",
        Summary = "Publishes MQTT request messages through a host-owned publisher.",
        IconKey = "send",
        PreferredNodeName = "publishMqtt",
        SuggestedEditorWidth = 420,
        Options =
        [
            new OptionDesignMetadata
            {
                Name = "publishTimeoutMilliseconds",
                Kind = OptionValueKind.Number,
                DisplayName = "Publish Timeout Milliseconds",
                DefaultValue = PublishDefaults.PublishTimeoutMilliseconds,
                Min = 1,
                HelperText = "Maximum time to wait for a publish operation."
            },
            BoundedCapacityOption(PublishDefaults.BoundedCapacity)
        ],
        Resources =
        [
            Resource(
                MqttCompositionResourceNames.Publisher,
                "Publisher",
                nameof(IMqttPublisher),
                "Keyed MQTT publisher used to send publish requests.",
                isRequired: true,
                order: 0),
            Resource(
                MqttCompositionResourceNames.Clock,
                "Clock",
                nameof(TimeProvider),
                "Optional keyed clock for deterministic publish diagnostics.",
                isRequired: false,
                order: 1)
        ],
        Ports =
        [
            InputPort(
                MqttCompositionPortNames.Input,
                nameof(MqttPublishRequest),
                "MQTT publish request."),
            OutputPort(
                MqttCompositionPortNames.Output,
                nameof(MqttPublishResult),
                "MQTT publish result.")
        ]
    };

    private static ComponentDesignMetadata CreateTriggerMetadata() => new()
    {
        Type = new ComponentType(MqttCompositionNodeTypes.Trigger),
        DisplayName = "MQTT Trigger",
        Category = "MQTT",
        Summary = "Subscribes through a host-owned trigger source and emits received MQTT messages.",
        IconKey = "radio-tower",
        PreferredNodeName = "triggerMqtt",
        SuggestedEditorWidth = 460,
        Options =
        [
            new OptionDesignMetadata
            {
                Name = "topicFilter",
                Kind = OptionValueKind.Text,
                DisplayName = "Topic Filter",
                IsRequired = true,
                HelperText = "MQTT subscription filter to open through the trigger source."
            },
            new OptionDesignMetadata
            {
                Name = "qualityOfService",
                Kind = OptionValueKind.Enum,
                DisplayName = "Quality Of Service",
                DefaultValue = TriggerDefaults.QualityOfService.ToString(),
                HelperText = "Requested subscription quality of service.",
                Choices = QualityOfServiceChoices()
            },
            new OptionDesignMetadata
            {
                Name = "receiveRetainedMessages",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Receive Retained Messages",
                DefaultValue = TriggerDefaults.ReceiveRetainedMessages,
                HelperText = "Request retained messages when opening the subscription."
            },
            new OptionDesignMetadata
            {
                Name = "retainAsPublished",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Retain As Published",
                DefaultValue = TriggerDefaults.RetainAsPublished,
                HelperText = "Request broker-provided retain flags exactly as published."
            },
            BoundedCapacityOption(TriggerDefaults.BoundedCapacity),
            new OptionDesignMetadata
            {
                Name = "mode",
                Kind = OptionValueKind.Enum,
                DisplayName = "Mode",
                DefaultValue = TriggerDefaults.Mode.ToString(),
                HelperText = "Trigger delivery mode.",
                Choices = TriggerModeChoices()
            },
            new OptionDesignMetadata
            {
                Name = "acknowledgement",
                Kind = OptionValueKind.Enum,
                DisplayName = "Acknowledgement",
                DefaultValue = TriggerDefaults.Acknowledgement.ToString(),
                HelperText = "Ack/nack policy for emitted messages.",
                Choices = AcknowledgementChoices()
            },
            new OptionDesignMetadata
            {
                Name = "responseTimeout",
                Kind = OptionValueKind.Duration,
                DisplayName = "Response Timeout",
                DefaultValue = TriggerDefaults.ResponseTimeout,
                Min = 0.000001,
                HelperText = "Timeout for request/reply responses; must be greater than zero."
            }
        ],
        Resources =
        [
            Resource(
                MqttCompositionResourceNames.TriggerSource,
                "Trigger Source",
                nameof(IMqttTriggerSource),
                "Keyed MQTT trigger source used to open subscriptions.",
                isRequired: true,
                order: 0),
            Resource(
                MqttCompositionResourceNames.Clock,
                "Clock",
                nameof(TimeProvider),
                "Optional keyed clock for deterministic trigger diagnostics and response timeouts.",
                isRequired: false,
                order: 1)
        ],
        Ports =
        [
            InputPort(
                MqttCompositionPortNames.Responses,
                nameof(MqttTriggerResponse),
                "Request/reply response message."),
            OutputPort(
                MqttCompositionPortNames.Output,
                nameof(MqttReceivedMessage),
                "Received MQTT message.")
        ]
    };

    private static OptionDesignMetadata BoundedCapacityOption(int defaultValue) => new()
    {
        Name = "boundedCapacity",
        Kind = OptionValueKind.Number,
        DisplayName = "Bounded Capacity",
        DefaultValue = defaultValue,
        Min = 1,
        HelperText = "Maximum queued messages."
    };

    private static ResourceDesignMetadata Resource(
        string name,
        string displayName,
        string valueType,
        string summary,
        bool isRequired,
        int order) => new()
        {
            Name = name,
            DisplayName = displayName,
            Order = order,
            Summary = summary,
            ValueType = valueType,
            IsRequired = isRequired
        };

    private static PortDesignMetadata InputPort(
        string name,
        string valueType,
        string summary) => new()
        {
            Name = new ComponentPortName(name),
            Direction = PortDirection.Input,
            DisplayName = name,
            Group = "Messages",
            Order = 0,
            Summary = summary,
            ValueType = valueType,
            IsPrimary = true
        };

    private static PortDesignMetadata OutputPort(
        string name,
        string valueType,
        string summary) => new()
        {
            Name = new ComponentPortName(name),
            Direction = PortDirection.Output,
            DisplayName = name,
            Group = "Results",
            Order = 1,
            Summary = summary,
            ValueType = valueType,
            IsPrimary = true
        };

    private static IReadOnlyList<OptionChoiceMetadata> QualityOfServiceChoices()
        =>
        [
            EnumChoice(MqttQualityOfService.AtMostOnce, "At Most Once"),
            EnumChoice(MqttQualityOfService.AtLeastOnce, "At Least Once"),
            EnumChoice(MqttQualityOfService.ExactlyOnce, "Exactly Once")
        ];

    private static IReadOnlyList<OptionChoiceMetadata> TriggerModeChoices()
        =>
        [
            EnumChoice(MqttTriggerMode.FireAndForget, "Fire And Forget"),
            EnumChoice(MqttTriggerMode.RequestReply, "Request Reply")
        ];

    private static IReadOnlyList<OptionChoiceMetadata> AcknowledgementChoices()
        =>
        [
            EnumChoice(MqttTriggerAcknowledgement.None, "None"),
            EnumChoice(MqttTriggerAcknowledgement.OnEmit, "On Emit"),
            EnumChoice(
                MqttTriggerAcknowledgement.OnSuccessfulResponse,
                "On Successful Response")
        ];

    private static OptionChoiceMetadata EnumChoice<TEnum>(
        TEnum value,
        string displayName)
        where TEnum : struct, Enum
        => new()
        {
            Value = value.ToString(),
            DisplayName = displayName
        };
}
