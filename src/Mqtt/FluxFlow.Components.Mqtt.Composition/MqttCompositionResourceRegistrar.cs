using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Mqtt.Composition;

internal sealed class MqttCompositionResourceRegistrar : IApplicationResourceRegistrar
{
    public void Register(ApplicationResourceRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Register(context.Services, context.Definition, context.HostServices);
    }

    internal static void Register(
        IServiceCollection services,
        ApplicationDefinition definition,
        IServiceProvider hostServices)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(hostServices);
        var resources = MqttCompositionResourceIndex.Create(definition);
        foreach (var resource in resources.OrderedResources)
        {
            if (string.Equals(
                    resource.Definition.Type,
                    "resilience.retry",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Resource type 'resilience.retry' is no longer supported. Use 'retry.policy'.");
            }

            switch (resource.Definition.Type)
            {
                case MqttComponentDefinition.ResourceTypes.Broker:
                    RegisterBroker(services, resource);
                    break;
                case MqttComponentDefinition.ResourceTypes.Retry:
                    RegisterRetry(services, resource);
                    break;
                case MqttComponentDefinition.ResourceTypes.Subscription:
                    RegisterSubscription(services, resource);
                    break;
                case MqttComponentDefinition.ResourceTypes.Client:
                    RegisterClient(services, resource, resources, hostServices);
                    break;
            }
        }
    }

    private static void RegisterBroker(IServiceCollection services, MqttIndexedResource resource)
    {
        MqttCompositionResourceValidator.ValidateProperties(
            resource,
            "Host",
            "Port",
            "Transport",
            "UseTls",
            "ServerName",
            "WebSocketPath");
        var configuration = MqttCompositionConfigurationConverter.Deserialize<MqttBrokerConfiguration>(
            resource.Definition.Properties);
        if (!HasKeyedService<MqttBrokerConfiguration>(services, resource.Address.Value))
            services.AddKeyedSingleton(resource.Address.Value, configuration);
    }

    private static void RegisterRetry(IServiceCollection services, MqttIndexedResource resource)
    {
        MqttCompositionResourceValidator.ValidateProperties(
            resource,
            "Strategy",
            "InitialDelay",
            "Increment",
            "MaximumDelay",
            "MaximumAttempts",
            "MaximumDuration",
            "ResetAfter",
            "JitterFactor",
            "RetryCategories");
        var policy = MqttCompositionConfigurationConverter.Deserialize<MqttRetryPolicy>(
            resource.Definition.Properties);
        if (!HasKeyedService<MqttRetryPolicy>(services, resource.Address.Value))
            services.AddKeyedSingleton(resource.Address.Value, policy);
    }

    private static void RegisterSubscription(
        IServiceCollection services,
        MqttIndexedResource resource)
    {
        MqttCompositionResourceValidator.ValidateProperties(
            resource,
            "TopicFilter",
            "Qos",
            "NoLocal",
            "RetainAsPublished",
            "RetainHandling");
        var subscription =
            MqttCompositionConfigurationConverter.Deserialize<MqttSubscriptionDefinition>(
                resource.Definition.Properties);
        if (!HasKeyedService<MqttSubscriptionDefinition>(services, resource.Address.Value))
            services.AddKeyedSingleton(resource.Address.Value, subscription);
    }

    private static void RegisterClient(
        IServiceCollection services,
        MqttIndexedResource resource,
        MqttCompositionResourceIndex resources,
        IServiceProvider hostServices)
    {
        MqttCompositionResourceValidator.ValidateProperties(
            resource,
            "ClientId",
            "Broker",
            "Credentials",
            "Username",
            "Password",
            "Certificates",
            "CleanStart",
            "KeepAlive",
            "LastWill",
            "AutoConnect",
            "Reconnect",
            "Subscriptions");

        var binding = MqttClientResourceBinding.Create(resource, resources);
        if (!HasKeyedService<MqttClientConfiguration>(services, resource.Address.Value))
        {
            services.AddKeyedSingleton<MqttClientConfiguration>(
                resource.Address.Value,
                (provider, _) => binding.CreateConfiguration(provider, hostServices));
        }

        if (!HasKeyedService<IMqttClientController>(services, resource.Address.Value))
        {
            services.AddKeyedSingleton<IMqttClientController>(
                resource.Address.Value,
                (provider, _) => new MqttClientController(
                    provider.GetRequiredKeyedService<MqttClientConfiguration>(resource.Address.Value),
                    ResolveTransportFactory(hostServices, resource.Address),
                    ResolveClock(hostServices, resource.Address)));
        }
    }

    private static IMqttTransportFactory ResolveTransportFactory(
        IServiceProvider hostServices,
        ApplicationAddress client)
        => hostServices.GetKeyedService<IMqttTransportFactory>(client.Value)
           ?? hostServices.GetService<IMqttTransportFactory>()
           ?? throw new InvalidOperationException(
               $"MQTT client resource '{client}' requires an {nameof(IMqttTransportFactory)} " +
               "registered for its resource address or as the host default.");

    private static TimeProvider ResolveClock(
        IServiceProvider hostServices,
        ApplicationAddress client)
        => hostServices.GetKeyedService<TimeProvider>(client.Value)
           ?? hostServices.GetService<TimeProvider>()
           ?? TimeProvider.System;

    private static bool HasKeyedService<TService>(IServiceCollection services, object key)
        => services.Any(descriptor =>
            descriptor.ServiceType == typeof(TService) &&
            descriptor.IsKeyedService &&
            Equals(descriptor.ServiceKey, key));
}
