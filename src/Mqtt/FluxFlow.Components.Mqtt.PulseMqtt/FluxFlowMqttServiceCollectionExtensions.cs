using FluxFlow.Components.Mqtt.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pulse.Mqtt.Transport;

namespace FluxFlow.Components.Mqtt.PulseMqtt;

public static class FluxFlowMqttServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowMqttClient(
        this IServiceCollection services,
        string name,
        PulseMqttClientOptions options,
        MqttClientRegistrationOptions? registrationOptions = null,
        TimeProvider? clock = null,
        IMqttTransportFactory? transportFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        return services.AddFluxFlowMqttClient(
            name,
            _ => options,
            registrationOptions,
            _ => clock,
            _ => transportFactory);
    }

    public static IServiceCollection AddFluxFlowMqttClient(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, PulseMqttClientOptions> optionsFactory,
        MqttClientRegistrationOptions? registrationOptions = null,
        Func<IServiceProvider, TimeProvider?>? clockFactory = null,
        Func<IServiceProvider, IMqttTransportFactory?>? transportFactory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(optionsFactory);

        var lifecycle = registrationOptions ?? new MqttClientRegistrationOptions();
        if (!lifecycle.StartWithHost && lifecycle.WaitForConnectedOnStart)
        {
            throw new ArgumentException(
                "WaitForConnectedOnStart requires StartWithHost.",
                nameof(registrationOptions));
        }

        var normalizedName = name.Trim();

        services.AddKeyedSingleton(normalizedName, (provider, _) => new PulseMqttClientRegistrationState(
            optionsFactory(provider)
                ?? throw new InvalidOperationException(
                    "MQTT client options factory returned null."),
            clockFactory?.Invoke(provider),
            transportFactory?.Invoke(provider)));

        services.AddKeyedSingleton<PulseMqttClient>(normalizedName, (provider, key) =>
        {
            var state = provider.GetRequiredKeyedService<PulseMqttClientRegistrationState>(key!);
            return new PulseMqttClient(
                state.Options,
                state.Clock,
                state.TransportFactory);
        });

        services.AddKeyedSingleton<IMqttPublisher>(
            normalizedName,
            (provider, key) => provider.GetRequiredKeyedService<PulseMqttClient>(key!));
        services.AddKeyedSingleton<IMqttTriggerSource>(
            normalizedName,
            (provider, key) => provider.GetRequiredKeyedService<PulseMqttClient>(key!));
        services.AddKeyedSingleton<IMqttClientHealthSource>(
            normalizedName,
            (provider, key) => provider.GetRequiredKeyedService<PulseMqttClient>(key!));

        if (lifecycle.StartWithHost)
        {
            services.AddSingleton<IHostedService>(
                provider => new PulseMqttClientLifetime(provider, normalizedName, lifecycle));
        }

        return services;
    }

    private sealed record PulseMqttClientRegistrationState(
        PulseMqttClientOptions Options,
        TimeProvider? Clock,
        IMqttTransportFactory? TransportFactory);

    private sealed class PulseMqttClientLifetime(
        IServiceProvider services,
        string name,
        MqttClientRegistrationOptions options)
        : IHostedService
    {
        private PulseMqttClient? _client;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _client = services.GetRequiredKeyedService<PulseMqttClient>(name);

            if (options.WaitForConnectedOnStart)
            {
                await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await _client.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_client is not null)
            {
                await _client.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
