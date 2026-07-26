using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Hosting;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Mqtt.Composition;

internal sealed class MqttCompositionResourceRegistrar : IApplicationResourceRegistrar
{
    public void Register(ApplicationResourceRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Register(context.Services, context.Definition);
    }

    internal static void Register(
        IServiceCollection services,
        ApplicationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);
        var resources = MqttCompositionResourceIndex.Create(definition);
        foreach (var resource in resources.OrderedResources)
        {
            switch (resource.Definition.Type)
            {
                case MqttCompositionResourceTypes.Broker:
                    RegisterBroker(services, resource);
                    break;
                case MqttCompositionResourceTypes.Retry:
                case MqttCompositionResourceTypes.LegacyRetry:
                    RegisterRetry(services, resource);
                    break;
                case MqttCompositionResourceTypes.Subscription:
                    RegisterSubscription(services, resource);
                    break;
                case MqttCompositionResourceTypes.Client:
                    RegisterClient(services, resource, resources);
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
            "UseTls",
            "ServerName");
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
        MqttCompositionResourceIndex resources)
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
                (provider, _) => binding.CreateConfiguration(provider));
        }

        if (!HasKeyedService<IMqttClientController>(services, resource.Address.Value))
        {
            services.AddKeyedSingleton<IMqttClientController>(
                resource.Address.Value,
                (provider, _) => new MqttClientController(
                    provider.GetRequiredKeyedService<MqttClientConfiguration>(resource.Address.Value),
                    ResolveTransportFactory(provider, resource.Address),
                    ResolveClock(provider, resource.Address)));
        }
    }

    private static IMqttTransportFactory ResolveTransportFactory(
        IServiceProvider provider,
        ApplicationAddress client)
        => provider.GetKeyedService<IMqttTransportFactory>(client.Value)
           ?? provider.GetService<IMqttTransportFactory>()
           ?? throw new InvalidOperationException(
               $"MQTT client resource '{client}' requires an {nameof(IMqttTransportFactory)} " +
               "registered for its resource address or as the host default.");

    private static TimeProvider ResolveClock(
        IServiceProvider provider,
        ApplicationAddress client)
        => provider.GetKeyedService<TimeProvider>(client.Value)
           ?? provider.GetService<TimeProvider>()
           ?? TimeProvider.System;

    private static bool HasKeyedService<TService>(IServiceCollection services, object key)
        => services.Any(descriptor =>
            descriptor.ServiceType == typeof(TService) &&
            descriptor.IsKeyedService &&
            Equals(descriptor.ServiceKey, key));
}
