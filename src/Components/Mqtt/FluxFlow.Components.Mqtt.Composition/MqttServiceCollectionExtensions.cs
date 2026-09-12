using System.Text.Json;
using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Materialization;
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
            .AddComponent(MqttComponents.MqttCommand)
            .AddComponent(MqttComponents.MqttPublish)
            .AddComponent(MqttComponents.MqttReceive)
            .AddComponent(MqttComponents.MqttEvents);
    }

    internal static void ConfigureControl(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<MqttRequestProcessing>(MqttComponentDefinition.Options.RequestProcessing);
        component.AddOption<MqttResultOrder>(MqttComponentDefinition.Options.ResultOrder);
        component.AddOption<int>(MqttComponentDefinition.Options.MaximumConcurrentRequests);
        component.AddOption<int>(MqttComponentDefinition.Options.MaximumPendingRequests);
        AddClient(component);
        component
            .UseFactory(MqttCompositionNodeFactories.CreateControlNodeAsync)
            .HasStandardPorts(new MqttClientRequestMaterializer().InputShape);
    }

    internal static void ConfigurePublish(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<int>(MqttComponentDefinition.Options.MaximumPendingRequests);
        AddClient(component);
        component
            .UseFactory(MqttCompositionNodeFactories.CreatePublishNodeAsync)
            .HasStandardPorts(new MqttPublishMessageMaterializer().InputShape);
    }

    internal static void ConfigureTrigger(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<JsonElement>(MqttComponentDefinition.Options.Subscription, isRequired: true);
        component.AddOption<MqttWorkflowAcknowledgement>(MqttComponentDefinition.Options.WorkflowAcknowledgement);
        component.AddOption<MqttBrokerAcknowledgement>(MqttComponentDefinition.Options.BrokerAcknowledgement);
        component.AddOption<TimeSpan>(MqttComponentDefinition.Options.OutcomeTimeout);
        component.AddOption<int>(MqttComponentDefinition.Options.MaximumPendingMessages);
        AddClient(component);
        component.AddResource<TimeProvider>(MqttComponentDefinition.Resources.Clock);
        component
            .UseFactory(MqttCompositionNodeFactories.CreateTriggerNodeAsync)
            .HasSignalInput(MqttComponentDefinition.Ports.Ack, static node => node.Ack)
            .HasSignalInput(MqttComponentDefinition.Ports.Nak, static node => node.Nak)
            .HasOutput(MqttComponentDefinition.Ports.Output, static node => node.Output)
            .HasEvents(MqttComponentDefinition.Ports.Events, static node => node.Events);
    }

    internal static void ConfigureEvents(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<int>(MqttComponentDefinition.Options.MaximumPendingEvents);
        AddClient(component);
        component
            .UseFactory(MqttCompositionNodeFactories.CreateEventsNodeAsync)
            .HasOutput(MqttComponentDefinition.Ports.Output, static node => node.Output)
            .HasEvents(MqttComponentDefinition.Ports.Events, static node => node.Events);
    }

    private static void AddClient(RuntimeComponentRegistrationBuilder component)
        => component.AddResource<IMqttClientController>(MqttComponentDefinition.Resources.Client, isRequired: true);
}
