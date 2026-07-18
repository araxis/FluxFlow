using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Transport;
using ProviderTransportFactory = Pulse.Mqtt.Transport.IMqttTransportFactory;

namespace FluxFlow.Components.Mqtt.PulseMqtt;

public sealed class PulseMqttTransportFactory : IMqttTransportFactory
{
    private readonly TimeProvider _clock;
    private readonly ProviderTransportFactory? _transportFactory;

    public PulseMqttTransportFactory(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    public PulseMqttTransportFactory(
        ProviderTransportFactory transportFactory,
        TimeProvider? clock = null)
    {
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        _clock = clock ?? TimeProvider.System;
    }

    public ValueTask<IMqttTransportSession> CreateAsync(
        MqttClientConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult<IMqttTransportSession>(
            new PulseMqttTransportSession(configuration, _clock, _transportFactory));
    }
}
