using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Options;

namespace FluxFlow.Components.Mqtt.Composition;

public sealed class MqttComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    private static readonly MqttControlOptions ControlDefaults = new();
    private static readonly MqttPublishCompositionOptions PublishDefaults = new();
    private static readonly MqttSubscriptionTriggerOptions TriggerDefaults = new()
    {
        TriggerId = "designer"
    };
    private static readonly MqttEventsCompositionOptions EventsDefaults = new();

    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        =>
        [
            CreateControlMetadata(),
            CreatePublishMetadata(),
            CreateTriggerMetadata(),
            CreateEventsMetadata()
        ];

    private static ComponentDesignMetadata CreateControlMetadata()
    {
        var builder = CreateBuilder(
            MqttCompositionNodeTypes.Control,
            "MQTT Control",
            "Executes lifecycle, status, publish, and subscription requests for one logical client.",
            "sliders-horizontal",
            "controlMqtt",
            460);
        builder
            .AddAttribute(ComponentDesignMetadataAttributeNames.Aliases, MqttCompositionNodeTypes.LegacyControl)
            .AddOption(EnumOption(
                "requestProcessing",
                "Request Processing",
                "Processing",
                OptionDesignMetadataAttributeValues.Primary,
                ControlDefaults.RequestProcessing,
                EnumChoices<MqttRequestProcessing>()))
            .AddOption(EnumOption(
                "resultOrder",
                "Result Order",
                "Processing",
                OptionDesignMetadataAttributeValues.Advanced,
                ControlDefaults.ResultOrder,
                EnumChoices<MqttResultOrder>()))
            .AddOption(NumberOption(
                "maximumConcurrentRequests",
                "Maximum Concurrent Requests",
                ControlDefaults.MaximumConcurrentRequests,
                "Runtime"))
            .AddOption(NumberOption(
                "maximumPendingRequests",
                "Maximum Pending Requests",
                ControlDefaults.MaximumPendingRequests,
                "Runtime"));
        AddClientResource(builder);
        AddMessagePorts<MqttClientRequest, MqttClientResult>(builder);
        return builder.Build();
    }

    private static ComponentDesignMetadata CreatePublishMetadata()
    {
        var builder = CreateBuilder(
            MqttCompositionNodeTypes.Publish,
            "MQTT Publish",
            "Publishes one exact-content MQTT message through a logical client.",
            "send",
            "publishMqtt",
            420);
        builder.AddOption(NumberOption(
            "maximumPendingRequests",
            "Maximum Pending Requests",
            PublishDefaults.MaximumPendingRequests,
            "Runtime"));
        AddClientResource(builder);
        AddMessagePorts<MqttPublishMessage, MqttClientResult>(builder);
        return builder.Build();
    }

    private static ComponentDesignMetadata CreateTriggerMetadata()
    {
        var builder = CreateBuilder(
            MqttCompositionNodeTypes.Trigger,
            "MQTT Trigger",
            "Emits received messages for named or inline subscriptions and accepts Ack/Nak signals.",
            "radio-tower",
            "triggerMqtt",
            500);
        builder
            .AddAttribute(ComponentDesignMetadataAttributeNames.Aliases, MqttCompositionNodeTypes.LegacyTrigger)
            .AddOption(
                "subscription",
                OptionValueKind.Json,
                displayName: "Subscription",
                helperText: "One named subscription, one inline subscription, or a mixed array.",
                isRequired: true,
                attributes: OptionAttributes(
                    "Subscription",
                    OptionDesignMetadataAttributeValues.Primary,
                    OptionDesignMetadataAttributeValues.Json))
            .AddOption(EnumOption(
                "workflowAcknowledgement",
                "Workflow Acknowledgement",
                "Delivery",
                OptionDesignMetadataAttributeValues.Primary,
                TriggerDefaults.WorkflowAcknowledgement,
                EnumChoices<MqttWorkflowAcknowledgement>()))
            .AddOption(EnumOption(
                "brokerAcknowledgement",
                "Broker Acknowledgement",
                "Delivery",
                OptionDesignMetadataAttributeValues.Advanced,
                TriggerDefaults.BrokerAcknowledgement,
                EnumChoices<MqttBrokerAcknowledgement>()))
            .AddOption(
                "outcomeTimeout",
                OptionValueKind.Duration,
                displayName: "Outcome Timeout",
                helperText: "Maximum wait for a workflow Ack or Nak outcome.",
                defaultValue: TriggerDefaults.OutcomeTimeout,
                min: 0.000001,
                attributes: OptionAttributes(
                    "Timeouts",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(NumberOption(
                "maximumPendingMessages",
                "Maximum Pending Messages",
                TriggerDefaults.MaximumPendingMessages,
                "Runtime"));
        AddClientResource(builder);
        AddClockResource(builder, order: 1);
        builder
            .AddInputPort(
                MqttCompositionPortNames.Ack,
                displayName: MqttCompositionPortNames.Ack,
                group: "Signals",
                order: 0,
                summary: "Acknowledges the pending delivery with the same trace identity.",
                valueType: nameof(Object),
                isPrimary: true,
                attributes: PortDesignMetadataAttributes.CreateSignal())
            .AddInputPort(
                MqttCompositionPortNames.Nak,
                displayName: MqttCompositionPortNames.Nak,
                group: "Signals",
                order: 1,
                summary: "Rejects the pending delivery with the same trace identity.",
                valueType: nameof(Object),
                attributes: PortDesignMetadataAttributes.CreateSignal())
            .AddOutputPort(
                MqttCompositionPortNames.Output,
                displayName: MqttCompositionPortNames.Output,
                group: "Messages",
                order: 2,
                summary: "Received MQTT application message.",
                valueType: nameof(MqttReceivedApplicationMessage),
                isPrimary: true);
        return builder.Build();
    }

    private static ComponentDesignMetadata CreateEventsMetadata()
    {
        var builder = CreateBuilder(
            MqttCompositionNodeTypes.Events,
            "MQTT Events",
            "Emits reliable logical-client connection and subscription events.",
            "activity",
            "mqttEvents",
            400);
        builder.AddOption(NumberOption(
            "maximumPendingEvents",
            "Maximum Pending Events",
            EventsDefaults.MaximumPendingEvents,
            "Runtime"));
        AddClientResource(builder);
        builder.AddOutputPort(
            MqttCompositionPortNames.Output,
            displayName: MqttCompositionPortNames.Output,
            group: "Events",
            order: 0,
            summary: "Logical MQTT client lifecycle event.",
            valueType: nameof(MqttClientEvent),
            isPrimary: true);
        return builder.Build();
    }

    private static ComponentDesignMetadataBuilder CreateBuilder(
        string type,
        string displayName,
        string summary,
        string iconKey,
        string preferredNodeName,
        int width)
        => new ComponentDesignMetadataBuilder(type)
            .WithDisplay(
                displayName,
                "MQTT",
                summary,
                iconKey,
                preferredNodeName,
                width);

    private static void AddClientResource(ComponentDesignMetadataBuilder builder)
        => builder.AddResource(
            MqttCompositionResourceNames.Client,
            displayName: "Client",
            order: 0,
            summary: "Logical MQTT client controller resource.",
            valueType: nameof(IMqttClientController),
            isRequired: true,
            attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                ResourceDesignMetadataAttributeValues.Client,
                keyPattern: "Resources.{name}"));

    private static void AddClockResource(ComponentDesignMetadataBuilder builder, int order)
        => builder.AddResource(
            MqttCompositionResourceNames.Clock,
            displayName: "Clock",
            order: order,
            summary: "Optional deterministic trigger timeout clock.",
            valueType: nameof(TimeProvider),
            attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                ResourceDesignMetadataAttributeValues.Clock,
                keyPattern: "Resources.{name}"));

    private static void AddMessagePorts<TInput, TOutput>(ComponentDesignMetadataBuilder builder)
        => builder
            .AddInputPort(
                MqttCompositionPortNames.Input,
                displayName: MqttCompositionPortNames.Input,
                group: "Messages",
                order: 0,
                summary: "Input message.",
                valueType: typeof(TInput).Name,
                isPrimary: true)
            .AddOutputPort(
                MqttCompositionPortNames.Output,
                displayName: MqttCompositionPortNames.Output,
                group: "Results",
                order: 1,
                summary: "Operation result.",
                valueType: typeof(TOutput).Name,
                isPrimary: true);

    private static OptionDesignMetadata NumberOption(
        string name,
        string displayName,
        int defaultValue,
        string section)
        => new()
        {
            Name = new ComponentOptionName(name),
            Kind = OptionValueKind.Number,
            DisplayName = new ComponentMetadataText(displayName),
            DefaultValue = defaultValue,
            Min = 1,
            HelperText = new ComponentMetadataText("Must be greater than zero."),
            Attributes = OptionDesignMetadataAttributes.CreateMap(
                section,
                OptionDesignMetadataAttributeValues.Advanced,
                OptionDesignMetadataAttributeValues.Number)
        };

    private static OptionDesignMetadata EnumOption<TEnum>(
        string name,
        string displayName,
        string section,
        string importance,
        TEnum defaultValue,
        IReadOnlyList<OptionChoiceMetadata> choices)
        where TEnum : struct, Enum
        => new()
        {
            Name = new ComponentOptionName(name),
            Kind = OptionValueKind.Enum,
            DisplayName = new ComponentMetadataText(displayName),
            HelperText = new ComponentMetadataText(
                $"Select the {displayName.ToLowerInvariant()} behavior."),
            DefaultValue = defaultValue.ToString(),
            Choices = choices,
            Attributes = OptionDesignMetadataAttributes.CreateMap(section, importance)
        };

    private static IReadOnlyList<OptionChoiceMetadata> EnumChoices<TEnum>()
        where TEnum : struct, Enum
        => Enum.GetValues<TEnum>()
            .Select(value => new OptionChoiceMetadata
            {
                Value = new ComponentOptionChoiceValue(value.ToString()),
                DisplayName = new ComponentMetadataText(value.ToString())
            })
            .ToArray();

    private static IReadOnlyDictionary<string, string> OptionAttributes(
        string section,
        string importance,
        string? editor = null)
        => OptionDesignMetadataAttributes.Create(section, importance, editor);
}
