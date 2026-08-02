using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Transport;
using MQTTnet;

namespace FluxFlow.Components.Mqtt.MqttNet;

public sealed class MqttNetTransportFactory : IMqttTransportFactory
{
    private readonly MqttClientFactory _builders;
    private readonly Func<IMqttClient> _createClient;
    private readonly TimeProvider _clock;

    public MqttNetTransportFactory()
        : this(new MqttClientFactory(), clock: null)
    {
    }

    public MqttNetTransportFactory(
        MqttClientFactory factory,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _builders = factory;
        _createClient = factory.CreateMqttClient;
        _clock = clock ?? TimeProvider.System;
    }

    internal MqttNetTransportFactory(
        MqttClientFactory builders,
        Func<IMqttClient> createClient,
        TimeProvider? clock = null)
    {
        _builders = builders ?? throw new ArgumentNullException(nameof(builders));
        _createClient = createClient ?? throw new ArgumentNullException(nameof(createClient));
        _clock = clock ?? TimeProvider.System;
    }

    public ValueTask<IMqttTransportSession> CreateAsync(
        MqttClientConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult<IMqttTransportSession>(new MqttNetTransportSession(
            configuration,
            _builders,
            _createClient(),
            _clock));
    }
}
