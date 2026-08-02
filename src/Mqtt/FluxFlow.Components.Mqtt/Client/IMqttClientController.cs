using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;

namespace FluxFlow.Components.Mqtt.Client;

public interface IMqttClientController : IAsyncDisposable
{
    string Name { get; }

    bool IsConnected { get; }

    MqttTransportCapabilities Capabilities { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    ValueTask<MqttClientResult> ExecuteAsync(
        MqttClientRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<IMqttTriggerRegistration> RegisterTriggerAsync(
        MqttTriggerRegistrationOptions options,
        CancellationToken cancellationToken = default);

    ValueTask<IMqttClientEventSubscription> SubscribeEventsAsync(
        int capacity = 128,
        CancellationToken cancellationToken = default);
}
