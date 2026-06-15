using FluxFlow.Components.Mqtt.Contracts;

namespace FluxFlow.Components.Mqtt.Options;

public sealed class MqttComponentOptions
{
    private IMqttClientFactory? _clientFactory;
    private TimeProvider _clock = TimeProvider.System;

    public TimeProvider Clock => _clock;

    public MqttComponentOptions UseClientFactory(IMqttClientFactory clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        return this;
    }

    public MqttComponentOptions UseClientFactory(
        Func<MqttConnectionProfile, CancellationToken, ValueTask<IMqttClientAdapter>> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        _clientFactory = new DelegateMqttClientFactory(
            async (context, cancellationToken) =>
                MqttClientLease.Owned(await clientFactory(context.Profile, cancellationToken)
                    .ConfigureAwait(false)));
        return this;
    }

    public MqttComponentOptions UseClientFactory(
        Func<MqttClientFactoryContext, CancellationToken, ValueTask<MqttClientLease>> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        _clientFactory = new DelegateMqttClientFactory(clientFactory);
        return this;
    }

    public MqttComponentOptions UseClock(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }

    internal IMqttClientFactory RequireClientFactory()
        => _clientFactory ?? throw new InvalidOperationException(
            "MQTT components require a client factory. Configure one before registering the module.");

    private sealed class DelegateMqttClientFactory(
        Func<MqttClientFactoryContext, CancellationToken, ValueTask<MqttClientLease>> create)
        : IMqttClientFactory
    {
        public ValueTask<MqttClientLease> CreateAsync(
            MqttClientFactoryContext context,
            CancellationToken cancellationToken = default)
            => create(context, cancellationToken);
    }
}
