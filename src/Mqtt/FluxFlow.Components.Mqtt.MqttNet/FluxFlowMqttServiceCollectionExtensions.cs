using FluxFlow.Components.Mqtt.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MQTTnet;

namespace FluxFlow.Components.Mqtt.MqttNet;

public static class FluxFlowMqttServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowMqttClient(
        this IServiceCollection services,
        string name,
        MqttNetClientOptions options,
        MqttClientRegistrationOptions? registrationOptions = null,
        TimeProvider? clock = null,
        MqttClientFactory? factory = null)
        => services.AddFluxFlowMqttClient(
            name,
            _ => options,
            registrationOptions,
            _ => clock,
            _ => factory);

    public static IServiceCollection AddFluxFlowMqttClient(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, MqttNetClientOptions> optionsFactory,
        MqttClientRegistrationOptions? registrationOptions = null,
        Func<IServiceProvider, TimeProvider?>? clockFactory = null,
        Func<IServiceProvider, MqttClientFactory?>? factory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(optionsFactory);

        var lifecycle = registrationOptions ?? new MqttClientRegistrationOptions();

        services.AddKeyedSingleton(name, (provider, _) => new MqttNetClientRegistrationState(
            optionsFactory(provider),
            clockFactory?.Invoke(provider),
            factory?.Invoke(provider)));

        services.AddKeyedSingleton<MqttNetClient>(name, (provider, key) =>
        {
            var state = provider.GetRequiredKeyedService<MqttNetClientRegistrationState>(key!);
            return new MqttNetClient(
                state.Options,
                state.Clock,
                state.Factory);
        });

        services.AddKeyedSingleton<IMqttPublisher>(
            name,
            (provider, key) => provider.GetRequiredKeyedService<MqttNetClient>(key!));
        services.AddKeyedSingleton<IMqttTriggerSource>(
            name,
            (provider, key) => provider.GetRequiredKeyedService<MqttNetClient>(key!));
        services.AddKeyedSingleton<IMqttClientHealthSource>(
            name,
            (provider, key) => provider.GetRequiredKeyedService<MqttNetClient>(key!));

        if (lifecycle.ConnectWithHost)
        {
            services.AddSingleton<IHostedService>(
                provider => new MqttNetClientLifetime(provider, name));
        }

        return services;
    }

    private sealed record MqttNetClientRegistrationState(
        MqttNetClientOptions Options,
        TimeProvider? Clock,
        MqttClientFactory? Factory);

    private sealed class MqttNetClientLifetime(IServiceProvider services, string name)
        : IHostedService
    {
        private MqttNetClient? _client;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _client = services.GetRequiredKeyedService<MqttNetClient>(name);
            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_client is not null)
            {
                await _client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
