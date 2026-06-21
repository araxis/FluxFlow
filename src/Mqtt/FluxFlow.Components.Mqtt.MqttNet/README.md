# FluxFlow.Components.Mqtt.MqttNet

MQTTnet-backed adapter for `FluxFlow.Components.Mqtt`.

The core MQTT package stays client-library-neutral: `MqttPublishNode` needs only
`IMqttPublisher`, and `MqttTriggerNode` needs only `IMqttTriggerSource`. This
package supplies one concrete session object, `MqttNetClient`, that implements:

- `IMqttPublisher`
- `IMqttTriggerSource`
- `IMqttClientHealthSource`

`MqttNetClient` owns MQTT client creation, broker connection, Last Will
configuration, reconnect, publish mapping, subscription streams, manual
acknowledgement, and health events.

## Usage

```csharp
var mqtt = new MqttNetClient(new MqttNetClientOptions
{
    Host = "localhost",
    Port = 1883,
    ClientId = "fluxflow-worker",
    LastWill = new MqttNetLastWillOptions
    {
        Topic = "workers/fluxflow/status",
        Payload = "offline"u8.ToArray(),
        Retain = true,
        QualityOfService = MqttQualityOfService.AtLeastOnce,
        ContentType = "text/plain"
    }
});

await mqtt.ConnectAsync();

var publish = new MqttPublishNode(mqtt);
var trigger = new MqttTriggerNode(mqtt, new MqttTriggerOptions
{
    TopicFilter = "commands/+",
    Mode = MqttTriggerMode.RequestReply,
    Acknowledgement = MqttTriggerAcknowledgement.OnSuccessfulResponse
});
```

Call `ConnectAsync` before using the object as a publisher or trigger source.
When the MQTT client is disconnected, publish and subscribe throw
`MqttClientUnavailableException`, which the core nodes translate into their
not-connected diagnostics.

## Dependency Injection

Register a named client session when the host wants DI-owned lifetime and keyed
MQTT roles:

```csharp
services.AddFluxFlowMqttClient(
    "primary",
    new MqttNetClientOptions
    {
        Host = "localhost",
        Port = 1883,
        ClientId = "fluxflow-worker"
    });
```

The extension registers one keyed `MqttNetClient` and exposes the same singleton
as keyed `IMqttPublisher`, `IMqttTriggerSource`, and `IMqttClientHealthSource`.

By default, the registration leaves connection lifetime to the composition
layer. Set `ConnectWithHost = true` when the host should call `ConnectAsync`
during start and `DisconnectAsync` during stop:

```csharp
services.AddFluxFlowMqttClient(
    "primary",
    options,
    new MqttClientRegistrationOptions { ConnectWithHost = true });
```

Workflow nodes should still be created and linked by the composition layer; the
registration owns only the adapter client session.

## Last Will

Last Will is adapter-owned because it is registered during MQTT `CONNECT`. It is
configured through `MqttNetClientOptions.LastWill`, not through publish or trigger
node options. Use a normal `MqttPublishRequest` for graceful online/offline status
messages.

## Acknowledgement

For trigger subscriptions with `Acknowledgement.None`, MQTTnet auto-acknowledges
received messages. For `OnEmit` and `OnSuccessfulResponse`, the adapter disables
auto acknowledgement and exposes `AckAsync`/`NackAsync` on each received context.
`NackAsync` maps to MQTTnet processing failure metadata and then completes the
MQTT acknowledgement path; MQTT broker retry behavior depends on broker and
protocol support.
