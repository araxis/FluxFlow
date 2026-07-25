using FluxFlow.Composition.Model;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Mqtt.Composition;

public static class MqttCompositionServiceCollectionExtensions
{
    public static IServiceCollection AddMqttCompositionResources(
        this IServiceCollection services,
        ApplicationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);

        MqttCompositionResourceRegistrar.Register(services, definition);
        return services;
    }
}
