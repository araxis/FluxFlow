using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Options;

namespace FluxFlow.Components.Mqtt.Composition;

public static partial class MqttComponentDefinition
{
    private static readonly MqttControlOptions ControlDefaults = new();
    private static readonly MqttPublishCompositionOptions PublishDefaults = new();
    private static readonly MqttSubscriptionTriggerOptions TriggerDefaults = new()
    {
        TriggerId = "designer"
    };
    private static readonly MqttEventsCompositionOptions EventsDefaults = new();

    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
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
            MqttComponentDefinition.Types.Control,
            "MQTT Command",
            "Executes lifecycle, status, publish, and subscription requests for one logical client.",
            "sliders-horizontal",
            "mqttCommand",
            460);
        builder
            .AddOption(EnumOption(
                Options.RequestProcessing,
                "Request Processing",
                "Processing",
                OptionDesignMetadataAttributeValues.Primary,
                ControlDefaults.RequestProcessing,
                EnumChoices<MqttRequestProcessing>()))
            .AddOption(EnumOption(
                Options.ResultOrder,
                "Result Order",
                "Processing",
                OptionDesignMetadataAttributeValues.Advanced,
                ControlDefaults.ResultOrder,
                EnumChoices<MqttResultOrder>()))
            .AddOption(NumberOption(
                Options.MaximumConcurrentRequests,
                "Maximum Concurrent Requests",
                ControlDefaults.MaximumConcurrentRequests,
                "Runtime"))
            .AddOption(NumberOption(
                Options.MaximumPendingRequests,
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
            MqttComponentDefinition.Types.Publish,
            "MQTT Publish",
            "Publishes one exact-content MQTT message through a logical client.",
            "send",
            "publishMqtt",
            420);
        builder.AddOption(NumberOption(
            Options.MaximumPendingRequests,
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
            MqttComponentDefinition.Types.Trigger,
            "MQTT Receive",
            "Emits received messages for named or inline subscriptions and accepts Ack/Nak signals.",
            "radio-tower",
            "mqttReceive",
            500);
        builder
            .AddOption(
                Options.Subscription,
                OptionValueKind.Json,
                displayName: "Subscription",
                helperText: "One named subscription, one inline subscription, or a mixed array.",
                isRequired: true,
                attributes: OptionAttributes(
                    "Subscription",
                    OptionDesignMetadataAttributeValues.Primary,
                    OptionDesignMetadataAttributeValues.Json))
            .AddOption(EnumOption(
                Options.WorkflowAcknowledgement,
                "Workflow Acknowledgement",
                "Delivery",
                OptionDesignMetadataAttributeValues.Primary,
                TriggerDefaults.WorkflowAcknowledgement,
                EnumChoices<MqttWorkflowAcknowledgement>()))
            .AddOption(EnumOption(
                Options.BrokerAcknowledgement,
                "Broker Acknowledgement",
                "Delivery",
                OptionDesignMetadataAttributeValues.Advanced,
                TriggerDefaults.BrokerAcknowledgement,
                EnumChoices<MqttBrokerAcknowledgement>()))
            .AddOption(
                Options.OutcomeTimeout,
                OptionValueKind.Duration,
                displayName: "Outcome Timeout",
                helperText: "Maximum wait for a workflow Ack or Nak outcome.",
                defaultValue: TriggerDefaults.OutcomeTimeout,
                min: 0.000001,
                attributes: OptionAttributes(
                    "Timeouts",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(NumberOption(
                Options.MaximumPendingMessages,
                "Maximum Pending Messages",
                TriggerDefaults.MaximumPendingMessages,
                "Runtime"));
        AddClientResource(builder);
        AddClockResource(builder, order: 1);
        builder
            .AddInputPort(
                MqttComponentDefinition.Ports.Ack,
                displayName: MqttComponentDefinition.Ports.Ack,
                group: "Signals",
                order: 0,
                summary: "Acknowledges the pending delivery with the same trace identity.",
                valueType: nameof(Object),
                isPrimary: true,
                attributes: PortDesignMetadataAttributes.CreateSignal())
            .AddInputPort(
                MqttComponentDefinition.Ports.Nak,
                displayName: MqttComponentDefinition.Ports.Nak,
                group: "Signals",
                order: 1,
                summary: "Rejects the pending delivery with the same trace identity.",
                valueType: nameof(Object),
                attributes: PortDesignMetadataAttributes.CreateSignal())
            .AddOutputPort(
                MqttComponentDefinition.Ports.Output,
                displayName: MqttComponentDefinition.Ports.Output,
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
            MqttComponentDefinition.Types.Events,
            "MQTT Events",
            "Emits reliable logical-client connection and subscription events.",
            "activity",
            "mqttEvents",
            400);
        builder.AddOption(NumberOption(
            Options.MaximumPendingEvents,
            "Maximum Pending Events",
            EventsDefaults.MaximumPendingEvents,
            "Runtime"));
        AddClientResource(builder);
        builder.AddOutputPort(
            MqttComponentDefinition.Ports.Output,
            displayName: MqttComponentDefinition.Ports.Output,
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
            MqttComponentDefinition.Resources.Client,
            displayName: Resources.Client,
            order: 0,
            summary: "Logical MQTT client controller resource.",
            valueType: nameof(IMqttClientController),
            isRequired: true,
            attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                ResourceDesignMetadataAttributeValues.Client,
                keyPattern: "Resources.{name}"));

    private static void AddClockResource(ComponentDesignMetadataBuilder builder, int order)
        => builder.AddResource(
            MqttComponentDefinition.Resources.Clock,
            displayName: Resources.Clock,
            order: order,
            summary: "Optional deterministic trigger timeout clock.",
            valueType: nameof(TimeProvider),
            attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                ResourceDesignMetadataAttributeValues.Clock,
                keyPattern: "Resources.{name}"));

    private static void AddMessagePorts<TInput, TOutput>(ComponentDesignMetadataBuilder builder)
        => builder
            .AddInputPort(
                MqttComponentDefinition.Ports.Input,
                displayName: MqttComponentDefinition.Ports.Input,
                group: "Messages",
                order: 0,
                summary: "Input message.",
                valueType: typeof(TInput).Name,
                isPrimary: true)
            .AddOutputPort(
                MqttComponentDefinition.Ports.Output,
                displayName: MqttComponentDefinition.Ports.Output,
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


    public static class Options
    {
        public const string RequestProcessing = "requestProcessing";
        public const string ResultOrder = "resultOrder";
        public const string MaximumConcurrentRequests = "maximumConcurrentRequests";
        public const string MaximumPendingRequests = "maximumPendingRequests";
        public const string Subscription = "subscription";
        public const string WorkflowAcknowledgement = "workflowAcknowledgement";
        public const string BrokerAcknowledgement = "brokerAcknowledgement";
        public const string OutcomeTimeout = "outcomeTimeout";
        public const string MaximumPendingMessages = "maximumPendingMessages";
        public const string MaximumPendingEvents = "maximumPendingEvents";
    }

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.Control =>
            [
                ComponentOptions.Metadata<MqttRequestProcessing>(Options.RequestProcessing),
                ComponentOptions.Metadata<MqttResultOrder>(Options.ResultOrder),
                ComponentOptions.Metadata<int>(Options.MaximumConcurrentRequests),
                ComponentOptions.Metadata<int>(Options.MaximumPendingRequests)
            ],
            Types.Publish =>
            [
                ComponentOptions.Metadata<int>(Options.MaximumPendingRequests)
            ],
            Types.Trigger =>
            [
                ComponentOptions.Metadata<JsonElement>(Options.Subscription, isRequired: true),
                ComponentOptions.Metadata<MqttWorkflowAcknowledgement>(Options.WorkflowAcknowledgement),
                ComponentOptions.Metadata<MqttBrokerAcknowledgement>(Options.BrokerAcknowledgement),
                ComponentOptions.Metadata<TimeSpan>(Options.OutcomeTimeout),
                ComponentOptions.Metadata<int>(Options.MaximumPendingMessages)
            ],
            Types.Events =>
            [
                ComponentOptions.Metadata<int>(Options.MaximumPendingEvents)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.Control =>
            [
                ComponentResources.Metadata<IMqttClientController>(Resources.Client, isRequired: true)
            ],
            Types.Publish =>
            [
                ComponentResources.Metadata<IMqttClientController>(Resources.Client, isRequired: true)
            ],
            Types.Trigger =>
            [
                ComponentResources.Metadata<IMqttClientController>(Resources.Client, isRequired: true),
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Events =>
            [
                ComponentResources.Metadata<IMqttClientController>(Resources.Client, isRequired: true)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string Control = "mqtt.command";
        public const string Publish = "mqtt.publish";
    
        public const string Trigger = "mqtt.receive";
        public const string Events = "mqtt.events";
    }

    public static class Ports
    {
        public const string Input = "Input";
    
        public const string Output = "Output";
    
        public const string Ack = "Ack";
    
        public const string Nak = "Nak";
    }

    public static class Resources
    {
        public const string Client = "Client";
    
        public const string Clock = "Clock";
    }


    public static class ResourceTypes
    {
        public const string Broker = "mqtt.broker";
    
        public const string Client = "mqtt.client";
    
        public const string Subscription = "mqtt.subscription";
    
        public const string Retry = "retry.policy";
    }
}
