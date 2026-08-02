using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Resilience;

namespace FluxFlow.Components.Mqtt.Client;

public sealed class MqttClientController : IMqttClientController
{
    private readonly MqttClientRuntime _runtime;

    public MqttClientController(
        MqttClientConfiguration configuration,
        IMqttTransportFactory transportFactory,
        TimeProvider? clock = null)
        : this(configuration, transportFactory, clock, RandomRetryJitterSource.Shared)
    {
    }

    public MqttClientController(
        MqttClientConfiguration configuration,
        IMqttTransportFactory transportFactory,
        TimeProvider? clock,
        IRetryJitterSource jitterSource)
    {
        _runtime = new MqttClientRuntime(configuration, transportFactory, clock, jitterSource);
    }

    public string Name => _runtime.Name;

    public bool IsConnected => _runtime.IsConnected;

    public MqttTransportCapabilities Capabilities => _runtime.Capabilities;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _runtime.StartAsync(cancellationToken);

    public ValueTask<MqttClientResult> ExecuteAsync(
        MqttClientRequest request,
        CancellationToken cancellationToken = default)
        => _runtime.ExecuteAsync(request, cancellationToken);

    public ValueTask<IMqttTriggerRegistration> RegisterTriggerAsync(
        MqttTriggerRegistrationOptions options,
        CancellationToken cancellationToken = default)
        => _runtime.RegisterTriggerAsync(options, cancellationToken);

    public ValueTask<IMqttClientEventSubscription> SubscribeEventsAsync(
        int capacity = 128,
        CancellationToken cancellationToken = default)
        => _runtime.SubscribeEventsAsync(capacity, cancellationToken);

    public ValueTask DisposeAsync() => _runtime.DisposeAsync();
}
