using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Mqtt.Designer;

public static class MqttDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddMqttDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignMetadata(MqttComponentDefinition.Types.Control, DescribeControl)
            .AddDesignMetadata(MqttComponentDefinition.Types.Publish, DescribePublish)
            .AddDesignMetadata(MqttComponentDefinition.Types.Trigger, DescribeReceive)
            .AddDesignMetadata(MqttComponentDefinition.Types.Events, DescribeEvents);
    }

    private static ComponentDesignMetadata DescribeControl(ComponentDesignMetadata metadata)
    {
        var defaults = new MqttControlOptions();
        return Describe(metadata, "MQTT Command", "Executes lifecycle, status, publish, and subscription requests for one logical client.",
            "sliders-horizontal", "mqttCommand", 460) with
        {
            Options = DescribeOptions(metadata, new()
            {
                [MqttComponentDefinition.Options.RequestProcessing] = option => Enum(option, "Request Processing", "Processing", "primary", defaults.RequestProcessing),
                [MqttComponentDefinition.Options.ResultOrder] = option => Enum(option, "Result Order", "Processing", "advanced", defaults.ResultOrder),
                [MqttComponentDefinition.Options.MaximumConcurrentRequests] = option => Number(option, "Maximum Concurrent Requests", "Maximum requests processed concurrently by the control component.", defaults.MaximumConcurrentRequests),
                [MqttComponentDefinition.Options.MaximumPendingRequests] = option => Number(option, "Maximum Pending Requests", "Capacity used for queued requests and reliable normal-data result output.", defaults.MaximumPendingRequests)
            }),
            Ports = DescribePorts(metadata, "Results", "Operation result.", "Best-effort MQTT command diagnostics.", MqttEventDesignMetadata.Command)
        };
    }

    private static ComponentDesignMetadata DescribePublish(ComponentDesignMetadata metadata)
    {
        var defaults = new MqttPublishCompositionOptions();
        return Describe(metadata, "MQTT Publish", "Materializes a workflow value and publishes it through a logical client.",
            "send", "publishMqtt", 420) with
        {
            Options = DescribeOptions(metadata, new()
            {
                [MqttComponentDefinition.Options.MaximumPendingRequests] = option => Number(option, "Maximum Pending Requests", "Capacity used for queued publish requests and reliable normal-data result output.", defaults.MaximumPendingRequests)
            }),
            Ports = DescribePorts(metadata, "Results", "Operation result.", "Best-effort MQTT publish diagnostics.", MqttEventDesignMetadata.Publish)
        };
    }

    private static ComponentDesignMetadata DescribeReceive(ComponentDesignMetadata metadata)
    {
        var defaults = new MqttSubscriptionTriggerOptions { TriggerId = "designer" };
        return Describe(metadata, "MQTT Receive", "Emits received messages for named or inline subscriptions and accepts Ack/Nak signals.",
            "radio-tower", "mqttReceive", 500) with
        {
            Options = DescribeOptions(metadata, new()
            {
                [MqttComponentDefinition.Options.Subscription] = option => option with
                {
                    DisplayName = new("Subscription"),
                    HelperText = new("One named subscription, one inline subscription, or a mixed array."),
                    Attributes = OptionAttributes("Subscription", "primary", "json")
                },
                [MqttComponentDefinition.Options.WorkflowAcknowledgement] = option => Enum(option, "Workflow Acknowledgement", "Delivery", "primary", defaults.WorkflowAcknowledgement),
                [MqttComponentDefinition.Options.BrokerAcknowledgement] = option => Enum(option, "Broker Acknowledgement", "Delivery", "advanced", defaults.BrokerAcknowledgement),
                [MqttComponentDefinition.Options.OutcomeTimeout] = option => option with
                {
                    DisplayName = new("Outcome Timeout"),
                    HelperText = new("Maximum wait for a workflow Ack or Nak outcome."),
                    DefaultValue = defaults.OutcomeTimeout,
                    Min = 0.000001,
                    Attributes = OptionAttributes("Timeouts", "advanced")
                },
                [MqttComponentDefinition.Options.MaximumPendingMessages] = option => Number(option, "Maximum Pending Messages", "Capacity used for pending broker messages and reliable normal-data trigger output.", defaults.MaximumPendingMessages)
            }),
            Ports = DescribePorts(metadata, "Messages", "Received MQTT application message.", "Best-effort MQTT receive diagnostics.", MqttEventDesignMetadata.Receive)
        };
    }

    private static ComponentDesignMetadata DescribeEvents(ComponentDesignMetadata metadata)
    {
        var defaults = new MqttEventsCompositionOptions();
        return Describe(metadata, "MQTT Events", "Emits reliable logical-client connection and subscription events.",
            "activity", "mqttEvents", 400) with
        {
            Options = DescribeOptions(metadata, new()
            {
                [MqttComponentDefinition.Options.MaximumPendingEvents] = option => Number(option, "Maximum Pending Events", "Capacity used for reliable normal-data logical-client event output.", defaults.MaximumPendingEvents)
            }),
            Ports = DescribePorts(metadata, "Events", "Logical MQTT client lifecycle event.", "Best-effort MQTT component diagnostics.", [])
        };
    }

    private static ComponentDesignMetadata Describe(ComponentDesignMetadata metadata,
        string displayName, string summary, string iconKey, string preferredName, int width)
        => metadata with
        {
            DisplayName = new(displayName),
            Category = new("MQTT"),
            Summary = new(summary),
            IconKey = new(iconKey),
            PreferredNodeName = new(preferredName),
            SuggestedEditorWidth = width,
            Resources = metadata.Resources.Select(resource => resource.Name.Value == MqttComponentDefinition.Resources.Client
                ? resource with
                {
                    DisplayName = new("Client"),
                    Order = 0,
                    Summary = new("Logical MQTT client controller resource."),
                    Attributes = ResourceAttributes("client")
                }
                : resource.Name.Value == MqttComponentDefinition.Resources.Clock
                    ? resource with
                    {
                        DisplayName = new("Clock"),
                        Order = 1,
                        Summary = new("Optional deterministic trigger timeout clock."),
                        Attributes = ResourceAttributes("clock")
                    }
                    : resource).ToArray()
        };

    private static IReadOnlyList<PortDesignMetadata> DescribePorts(ComponentDesignMetadata metadata,
        string outputGroup, string outputSummary, string eventSummary, IReadOnlyList<EventDesignMetadata> eventShapes)
    {
        var inputCount = metadata.Ports.Count(port => port.Direction == PortDirection.Input);
        var hints = new Dictionary<string, (string Group, int Order, string Summary, bool Primary)>(StringComparer.Ordinal)
        {
            [MqttComponentDefinition.Ports.Input] = ("Messages", 0, "Input message.", true),
            [MqttComponentDefinition.Ports.Ack] = ("Signals", 0, "Acknowledges the pending delivery with the same trace identity.", true),
            [MqttComponentDefinition.Ports.Nak] = ("Signals", 1, "Rejects the pending delivery with the same trace identity.", false),
            [MqttComponentDefinition.Ports.Output] = (outputGroup, inputCount, outputSummary, true),
            [MqttComponentDefinition.Ports.Events] = ("Diagnostics", inputCount + 1, eventSummary, false)
        };
        return metadata.Ports.Select(port => hints.TryGetValue(port.Name.Value, out var hint)
            ? port with
            {
                Group = new(hint.Group),
                Order = hint.Order,
                Summary = new(hint.Summary),
                IsPrimary = hint.Primary,
                EventShapes = port.Name.Value == MqttComponentDefinition.Ports.Events ? eventShapes : port.EventShapes
            } : port).ToArray();
    }

    private static IReadOnlyList<OptionDesignMetadata> DescribeOptions(ComponentDesignMetadata metadata,
        Dictionary<string, Func<OptionDesignMetadata, OptionDesignMetadata>> hints)
        => metadata.Options.Select(option => hints.TryGetValue(option.Name.Value, out var describe)
            ? describe(option) : option).ToArray();

    private static OptionDesignMetadata Number(OptionDesignMetadata option, string label, string help, int value)
        => option with
        {
            DisplayName = new(label),
            HelperText = new(help),
            DefaultValue = value,
            Min = 1,
            Attributes = OptionAttributes("Runtime", "advanced", "number")
        };

    private static OptionDesignMetadata Enum<TEnum>(OptionDesignMetadata option,
        string label, string section, string importance, TEnum value) where TEnum : struct, System.Enum
        => option with
        {
            DisplayName = new(label),
            HelperText = new($"Select the {label.ToLowerInvariant()} behavior."),
            DefaultValue = value.ToString(),
            Attributes = OptionAttributes(section, importance),
            Choices = System.Enum.GetValues<TEnum>().Select(choice => new OptionChoiceMetadata
            {
                Value = new(choice.ToString()),
                DisplayName = new(choice.ToString())
            }).ToArray()
        };

    private static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> OptionAttributes(
        string section, string importance, string? editor = null)
    {
        var attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>
        {
            [new(OptionDesignMetadataAttributeNames.Section)] = new(section),
            [new(OptionDesignMetadataAttributeNames.Importance)] = new(importance)
        };
        if (editor is not null) attributes.Add(new(OptionDesignMetadataAttributeNames.Editor), new(editor));
        return attributes;
    }

    private static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> ResourceAttributes(string picker)
        => new Dictionary<ComponentAttributeName, ComponentAttributeValue>
        {
            [new(ResourceDesignMetadataAttributeNames.Ownership)] = new(ResourceDesignMetadataAttributeValues.HostOwned),
            [new(ResourceDesignMetadataAttributeNames.PickerKind)] = new(picker),
            [new(ResourceDesignMetadataAttributeNames.KeyPattern)] = new("Resources.{name}")
        };
}
