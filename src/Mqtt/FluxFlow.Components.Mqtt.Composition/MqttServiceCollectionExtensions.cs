using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Mqtt.Composition;

public static class MqttServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddMqtt(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddApplicationResourceRegistrar(MqttResources.Registrar);
        return builder
            .AddDesignedComponent(MqttComponents.MqttCommand)
            .AddDesignedComponent(MqttComponents.MqttPublish)
            .AddDesignedComponent(MqttComponents.MqttReceive)
            .AddDesignedComponent(MqttComponents.MqttEvents);
    }

    internal static void ConfigureControl(ComponentRegistrationBuilder component)
    {
        var defaults = new MqttControlOptions();
        ConfigureCommon(component, "MQTT Command", "Executes lifecycle, status, publish, and subscription requests for one logical client.", "sliders-horizontal", "mqttCommand", 460);
        AddEnum(component, MqttComponentDefinition.Options.RequestProcessing, "Request Processing", "Processing", OptionDesignMetadataAttributeValues.Primary, defaults.RequestProcessing);
        AddEnum(component, MqttComponentDefinition.Options.ResultOrder, "Result Order", "Processing", OptionDesignMetadataAttributeValues.Advanced, defaults.ResultOrder);
        AddNumber(component, MqttComponentDefinition.Options.MaximumConcurrentRequests, "Maximum Concurrent Requests", "Maximum requests processed concurrently by the control component.", defaults.MaximumConcurrentRequests);
        AddNumber(component, MqttComponentDefinition.Options.MaximumPendingRequests, "Maximum Pending Requests", "Capacity used for queued requests and reliable normal-data result output.", defaults.MaximumPendingRequests);
        AddClient(component);
        component
            .UseFactory(MqttCompositionNodeFactories.CreateControlNodeAsync)
            .HasInput(MqttComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Input message.", true)
            .HasOutput(MqttComponentDefinition.Ports.Output, static node => node.Output, "Output", "Results", 1, "Operation result.", true)
            .HasEvents(MqttComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort MQTT command diagnostics.");
    }

    internal static void ConfigurePublish(ComponentRegistrationBuilder component)
    {
        var defaults = new MqttPublishCompositionOptions();
        ConfigureCommon(component, "MQTT Publish", "Publishes one exact-content MQTT message through a logical client.", "send", "publishMqtt", 420);
        AddNumber(component, MqttComponentDefinition.Options.MaximumPendingRequests, "Maximum Pending Requests", "Capacity used for queued publish requests and reliable normal-data result output.", defaults.MaximumPendingRequests);
        AddClient(component);
        component
            .UseFactory(MqttCompositionNodeFactories.CreatePublishNodeAsync)
            .HasInput(MqttComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Input message.", true)
            .HasOutput(MqttComponentDefinition.Ports.Output, static node => node.Output, "Output", "Results", 1, "Operation result.", true)
            .HasEvents(MqttComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort MQTT publish diagnostics.");
    }

    internal static void ConfigureTrigger(ComponentRegistrationBuilder component)
    {
        var defaults = new MqttSubscriptionTriggerOptions { TriggerId = "designer" };
        ConfigureCommon(component, "MQTT Receive", "Emits received messages for named or inline subscriptions and accepts Ack/Nak signals.", "radio-tower", "mqttReceive", 500);
        component.AddOption<JsonElement>(MqttComponentDefinition.Options.Subscription, OptionValueKind.Json, "Subscription", "One named subscription, one inline subscription, or a mixed array.", true, section: "Subscription", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Json);
        AddEnum(component, MqttComponentDefinition.Options.WorkflowAcknowledgement, "Workflow Acknowledgement", "Delivery", OptionDesignMetadataAttributeValues.Primary, defaults.WorkflowAcknowledgement);
        AddEnum(component, MqttComponentDefinition.Options.BrokerAcknowledgement, "Broker Acknowledgement", "Delivery", OptionDesignMetadataAttributeValues.Advanced, defaults.BrokerAcknowledgement);
        component.AddOption<TimeSpan>(MqttComponentDefinition.Options.OutcomeTimeout, OptionValueKind.Duration, "Outcome Timeout", "Maximum wait for a workflow Ack or Nak outcome.", defaultValue: defaults.OutcomeTimeout, min: 0.000001, section: "Timeouts", importance: OptionDesignMetadataAttributeValues.Advanced);
        AddNumber(component, MqttComponentDefinition.Options.MaximumPendingMessages, "Maximum Pending Messages", "Capacity used for pending broker messages and reliable normal-data trigger output.", defaults.MaximumPendingMessages);
        AddClient(component);
        component.AddResource<TimeProvider>(MqttComponentDefinition.Resources.Clock, "Clock", 1, "Optional deterministic trigger timeout clock.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
        component
            .UseFactory(MqttCompositionNodeFactories.CreateTriggerNodeAsync)
            .HasSignalInput(MqttComponentDefinition.Ports.Ack, static node => node.Ack, "Ack", "Signals", 0, "Acknowledges the pending delivery with the same trace identity.", true)
            .HasSignalInput(MqttComponentDefinition.Ports.Nak, static node => node.Nak, "Nak", "Signals", 1, "Rejects the pending delivery with the same trace identity.")
            .HasOutput(MqttComponentDefinition.Ports.Output, static node => node.Output, "Output", "Messages", 2, "Received MQTT application message.", true)
            .HasEvents(MqttComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 3, "Best-effort MQTT receive diagnostics.");
    }

    internal static void ConfigureEvents(ComponentRegistrationBuilder component)
    {
        var defaults = new MqttEventsCompositionOptions();
        ConfigureCommon(component, "MQTT Events", "Emits reliable logical-client connection and subscription events.", "activity", "mqttEvents", 400);
        AddNumber(component, MqttComponentDefinition.Options.MaximumPendingEvents, "Maximum Pending Events", "Capacity used for reliable normal-data logical-client event output.", defaults.MaximumPendingEvents);
        AddClient(component);
        component
            .UseFactory(MqttCompositionNodeFactories.CreateEventsNodeAsync)
            .HasOutput(MqttComponentDefinition.Ports.Output, static node => node.Output, "Output", "Events", 0, "Logical MQTT client lifecycle event.", true)
            .HasEvents(MqttComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 1, "Best-effort MQTT component diagnostics.");
    }

    private static void ConfigureCommon(ComponentRegistrationBuilder component, string displayName, string summary, string iconKey, string preferredNodeName, int width)
    {
        component.WithDisplay(displayName, "MQTT", summary, iconKey, preferredNodeName, width);
    }

    private static void AddClient(ComponentRegistrationBuilder component)
        => component.AddResource<IMqttClientController>(MqttComponentDefinition.Resources.Client, "Client", 0, "Logical MQTT client controller resource.", true, nameof(IMqttClientController), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Client, keyPattern: "Resources.{name}");

    private static void AddNumber(
        ComponentRegistrationBuilder component,
        string name,
        string displayName,
        string helperText,
        int defaultValue)
        => component.AddOption<int>(name, OptionValueKind.Number, displayName, helperText, defaultValue: defaultValue, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);

    private static void AddEnum<TEnum>(ComponentRegistrationBuilder component, string name, string displayName, string section, string importance, TEnum defaultValue)
        where TEnum : struct, Enum
    {
        component.AddOption<TEnum>(name, OptionValueKind.Enum, displayName, $"Select the {displayName.ToLowerInvariant()} behavior.", defaultValue: defaultValue.ToString(), section: section, importance: importance);
        foreach (var value in Enum.GetValues<TEnum>())
            component.AddOptionChoice(name, value.ToString(), value.ToString());
    }
}
