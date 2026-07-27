using FluxFlow.Components.Designer;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Mqtt.Composition;

public static class MqttServiceCollectionExtensions
{
    private static readonly Lazy<IReadOnlyCollection<ComponentDesignDeclaration>> DeclarationSet =
        new(CreateDeclarations);

    internal static IReadOnlyCollection<ComponentDesignDeclaration> Declarations =>
        DeclarationSet.Value;

    private static IReadOnlyCollection<ComponentDesignDeclaration> CreateDeclarations() =>
        ComponentDesignDeclaration.CreateRange(
            [
                ControlDescriptor,
                PublishDescriptor,
                TriggerDescriptor,
                EventsDescriptor
            ],
            MqttComponentDefinition.CreateMetadata());

    internal static ComponentDescriptor ControlDescriptor { get; } = new(
        MqttComponentDefinition.Types.Control,
        MqttCompositionNodeFactories.CreateControlNodeAsync,
        inputs: [ComponentPorts.Metadata<MqttClientRequest>(MqttComponentDefinition.Ports.Input)],
        outputs: [ComponentPorts.Metadata<MqttClientResult>(MqttComponentDefinition.Ports.Output)],
        options: MqttComponentDefinition.CreateOptions(MqttComponentDefinition.Types.Control),
        resources: MqttComponentDefinition.CreateResources(MqttComponentDefinition.Types.Control));
    internal static ComponentDescriptor PublishDescriptor { get; } = new(
        MqttComponentDefinition.Types.Publish,
        MqttCompositionNodeFactories.CreatePublishNodeAsync,
        inputs: [ComponentPorts.Metadata<MqttPublishMessage>(MqttComponentDefinition.Ports.Input)],
        outputs: [ComponentPorts.Metadata<MqttClientResult>(MqttComponentDefinition.Ports.Output)],
        options: MqttComponentDefinition.CreateOptions(MqttComponentDefinition.Types.Publish),
        resources: MqttComponentDefinition.CreateResources(MqttComponentDefinition.Types.Publish));
    internal static ComponentDescriptor TriggerDescriptor { get; } = new(
        MqttComponentDefinition.Types.Trigger,
        MqttCompositionNodeFactories.CreateTriggerNodeAsync,
        inputs:
        [
            ComponentPorts.SignalMetadata(MqttComponentDefinition.Ports.Ack),
            ComponentPorts.SignalMetadata(MqttComponentDefinition.Ports.Nak)
        ],
        outputs:
        [
            ComponentPorts.Metadata<MqttReceivedApplicationMessage>(MqttComponentDefinition.Ports.Output)
        ],
        options: MqttComponentDefinition.CreateOptions(MqttComponentDefinition.Types.Trigger),
        resources: MqttComponentDefinition.CreateResources(MqttComponentDefinition.Types.Trigger));
    internal static ComponentDescriptor EventsDescriptor { get; } = new(
        MqttComponentDefinition.Types.Events,
        MqttCompositionNodeFactories.CreateEventsNodeAsync,
        outputs: [ComponentPorts.Metadata<MqttClientEvent>(MqttComponentDefinition.Ports.Output)],
        options: MqttComponentDefinition.CreateOptions(MqttComponentDefinition.Types.Events),
        resources: MqttComponentDefinition.CreateResources(MqttComponentDefinition.Types.Events));

    public static IServiceCollection AddMqttComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclarations(Declarations);
        services.AddApplicationResourceRegistrar<MqttCompositionResourceRegistrar>();
        return services;
    }
}
