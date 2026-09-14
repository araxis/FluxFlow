using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Mqtt.Composition;

public static class MqttServiceCollectionExtensions
{
    public static IServiceCollection AddMqtt(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddApplicationResourceRegistrar(MqttResources.Registrar);
        return builder
            .AddComponent(MqttComponentDefinition.Types.Control, MqttCompositionNodeFactories.CreateControlNodeAsync)
            .AddComponent(MqttComponentDefinition.Types.Publish, MqttCompositionNodeFactories.CreatePublishNodeAsync)
            .AddComponent(MqttComponentDefinition.Types.Trigger, MqttCompositionNodeFactories.CreateTriggerNodeAsync)
            .AddComponent(MqttComponentDefinition.Types.Events, MqttCompositionNodeFactories.CreateEventsNodeAsync);
    }

}
