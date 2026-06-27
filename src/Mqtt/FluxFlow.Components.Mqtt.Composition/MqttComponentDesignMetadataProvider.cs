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

    private static ComponentDesignMetadata CreatePublishMetadata()
    {
        var builder = CreateMqttMetadataBuilder(
            MqttCompositionNodeTypes.Publish,
            "MQTT Publish",
            "Publishes MQTT request messages through a host-owned publisher.",
            "send",
            "publishMqtt",
            suggestedEditorWidth: 420);

        builder
            .AddOption(
                "publishTimeoutMilliseconds",
                OptionValueKind.Number,
                displayName: "Publish Timeout Milliseconds",
                helperText: "Maximum time to wait for a publish operation.",
                defaultValue: PublishDefaults.PublishTimeoutMilliseconds,
                min: 1)
            .AddOption(BoundedCapacityOption(PublishDefaults.BoundedCapacity))
            .AddResource(
                MqttCompositionResourceNames.Publisher,
                displayName: "Publisher",
                order: 0,
                summary: "Keyed MQTT publisher used to send publish requests.",
                valueType: nameof(IMqttPublisher),
                isRequired: true)
            .AddResource(
                MqttCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 1,
                summary: "Optional keyed clock for deterministic publish diagnostics.",
                valueType: nameof(TimeProvider));

        AddPorts(
            builder,
            MqttCompositionPortNames.Input,
            nameof(MqttPublishRequest),
            "MQTT publish request.",
            nameof(MqttPublishResult),
            "MQTT publish result.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateTriggerMetadata()
    {
        var builder = CreateMqttMetadataBuilder(
            MqttCompositionNodeTypes.Trigger,
            "MQTT Trigger",
            "Subscribes through a host-owned trigger source and emits received MQTT messages.",
            "radio-tower",
            "triggerMqtt",
            suggestedEditorWidth: 460);

        builder
            .AddOption(
                "topicFilter",
                OptionValueKind.Text,
                displayName: "Topic Filter",
                helperText: "MQTT subscription filter to open through the trigger source.",
                isRequired: true)
            .AddOption(
                "qualityOfService",
                OptionValueKind.Enum,
                displayName: "Quality Of Service",
                helperText: "Requested subscription quality of service.",
                defaultValue: TriggerDefaults.QualityOfService.ToString(),
                choices: QualityOfServiceChoices())
            .AddOption(
                "receiveRetainedMessages",
                OptionValueKind.Boolean,
                displayName: "Receive Retained Messages",
                helperText: "Request retained messages when opening the subscription.",
                defaultValue: TriggerDefaults.ReceiveRetainedMessages)
            .AddOption(
                "retainAsPublished",
                OptionValueKind.Boolean,
                displayName: "Retain As Published",
                helperText: "Request broker-provided retain flags exactly as published.",
                defaultValue: TriggerDefaults.RetainAsPublished)
            .AddOption(BoundedCapacityOption(TriggerDefaults.BoundedCapacity))
            .AddOption(
                "mode",
                OptionValueKind.Enum,
                displayName: "Mode",
                helperText: "Trigger delivery mode.",
                defaultValue: TriggerDefaults.Mode.ToString(),
                choices: TriggerModeChoices())
            .AddOption(
                "acknowledgement",
                OptionValueKind.Enum,
                displayName: "Acknowledgement",
                helperText: "Ack/nack policy for emitted messages.",
                defaultValue: TriggerDefaults.Acknowledgement.ToString(),
                choices: AcknowledgementChoices())
            .AddOption(
                "responseTimeout",
                OptionValueKind.Duration,
                displayName: "Response Timeout",
                helperText: "Timeout for request/reply responses; must be greater than zero.",
                defaultValue: TriggerDefaults.ResponseTimeout,
                min: 0.000001)
            .AddResource(
                MqttCompositionResourceNames.TriggerSource,
                displayName: "Trigger Source",
                order: 0,
                summary: "Keyed MQTT trigger source used to open subscriptions.",
                valueType: nameof(IMqttTriggerSource),
                isRequired: true)
            .AddResource(
                MqttCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 1,
                summary: "Optional keyed clock for deterministic trigger diagnostics and response timeouts.",
                valueType: nameof(TimeProvider));

        AddPorts(
            builder,
            MqttCompositionPortNames.Responses,
            nameof(MqttTriggerResponse),
            "Request/reply response message.",
            nameof(MqttReceivedMessage),
            "Received MQTT message.");

        return builder.Build();
    }

    private static ComponentDesignMetadataBuilder CreateMqttMetadataBuilder(
        string type,
        string displayName,
        string summary,
        string iconKey,
        string preferredNodeName,
        int suggestedEditorWidth)
        => new ComponentDesignMetadataBuilder(type)
            .WithDisplay(
                displayName: displayName,
                category: "MQTT",
                summary: summary,
                iconKey: iconKey,
                preferredNodeName: preferredNodeName,
                suggestedEditorWidth: suggestedEditorWidth);

    private static OptionDesignMetadata BoundedCapacityOption(int defaultValue) => new()
    {
        Name = new ComponentOptionName("boundedCapacity"),
        Kind = OptionValueKind.Number,
        DisplayName = new ComponentMetadataText("Bounded Capacity"),
        DefaultValue = defaultValue,
        Min = 1,
        HelperText = new ComponentMetadataText("Maximum queued messages.")
    };

    private static void AddPorts(
        ComponentDesignMetadataBuilder builder,
        string inputPortName,
        string inputType,
        string inputSummary,
        string outputType,
        string outputSummary)
        => builder
            .AddInputPort(
                inputPortName,
                displayName: inputPortName,
                group: "Messages",
                order: 0,
                summary: inputSummary,
                valueType: inputType,
                isPrimary: true)
            .AddOutputPort(
                MqttCompositionPortNames.Output,
                displayName: MqttCompositionPortNames.Output,
                group: "Results",
                order: 1,
                summary: outputSummary,
                valueType: outputType,
                isPrimary: true);

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
            Value = new ComponentOptionChoiceValue(value.ToString()),
            DisplayName = new ComponentMetadataText(displayName)
        };
}
