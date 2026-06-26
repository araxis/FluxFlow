# FluxFlow.Components.Mqtt.PulseMqtt

Pulse MQTT-backed adapter for `FluxFlow.Components.Mqtt`.

The core MQTT package stays client-library-neutral: `MqttPublishNode` needs only
`IMqttPublisher`, and `MqttTriggerNode` needs only `IMqttTriggerSource`. This
package supplies one concrete session object, `PulseMqttClient`, that implements:

- `IMqttPublisher`
- `IMqttTriggerSource`
- `IMqttClientHealthSource`

`PulseMqttClient` owns Pulse MQTT client creation, transport configuration,
start/stop, publish mapping, route-stream trigger subscriptions, Last Will
configuration, and health events.

## Usage

```csharp
var mqtt = new PulseMqttClient(new PulseMqttClientOptions
{
    Host = "localhost",
    Port = 1883,
    ClientId = "fluxflow-worker",
    LastWill = new PulseMqttLastWillOptions
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

Use `ConnectAsync` when the caller needs the adapter to wait for a live session.
Use `StartAsync` when the caller wants Pulse MQTT's resilient background
connection lifecycle and will observe health events until the session becomes
connected.

By default, FluxFlow publish semantics stay strict: publishing while disconnected
throws `MqttClientUnavailableException`. Set
`AllowOfflinePublishQueue = true` to opt into Pulse MQTT's offline publish queue.

## Dependency Injection

Register a named client session when the host wants DI-owned lifetime and keyed
MQTT roles:

```csharp
services.AddFluxFlowMqttClient(
    "primary",
    new PulseMqttClientOptions
    {
        Host = "localhost",
        Port = 1883,
        ClientId = "fluxflow-worker"
    });
```

The extension registers one keyed `PulseMqttClient` and exposes the same
singleton as keyed `IMqttPublisher`, `IMqttTriggerSource`, and
`IMqttClientHealthSource`.
The registration helpers reject null service collections, blank keys, null
direct options, null options factories, and null options factory results before
creating the keyed client session.

By default, the registration does not add hosted lifetime. Set
`StartWithHost = true` when the host should start and stop the client session:

```csharp
services.AddFluxFlowMqttClient(
    "primary",
    options,
    new MqttClientRegistrationOptions { StartWithHost = true });
```

Use `WaitForConnectedOnStart = true` only when application startup should wait for
an established connection. Workflow nodes should still be created and linked by the
composition layer; the registration owns only the adapter client session.

## Composition

This package does not expose `FluxFlow.Composition` node factories. It owns the
Pulse MQTT-backed client session, resilient connection lifecycle, durable store
options, and DI registration only.

Use `FluxFlow.Components.Mqtt.Composition` for `mqtt.publish` and `mqtt.trigger`
composition. That package consumes host-owned `IMqttPublisher`,
`IMqttTriggerSource`, and optional `IMqttClientHealthSource` resources provided
by this adapter package.

## Durable Stores

Durable message and session stores are adapter-owned. Provide Pulse MQTT store
implementations through `PulseMqttClientOptions.MessageStore` and
`PulseMqttClientOptions.SessionStore`. A message store is accepted only when
`AllowOfflinePublishQueue = true`, because the core FluxFlow publish contract stays
strict unless the host explicitly opts into offline queueing.

## Last Will

Last Will is adapter-owned because it is registered during MQTT `CONNECT`. It is
configured through `PulseMqttClientOptions.LastWill`, not through publish or
trigger node options. Use a normal `MqttPublishRequest` for graceful
online/offline status messages.

## Acknowledgement

`MqttTriggerAcknowledgement.None` uses Pulse MQTT's normal route stream, where
protocol acknowledgement is completed inside the client after local delivery.
`OnEmit` and `OnSuccessfulResponse` use Pulse MQTT acknowledged route streams
and expose broker acknowledgement through `IMqttReceivedContext.AckAsync` and
`NackAsync`.

Pulse acknowledged route streams are single-owner for each matching publish.
Avoid overlapping manual-ack subscriptions on the same `PulseMqttClient` when
each route must receive the same broker message; use `Acknowledgement.None` for
Pulse's normal managed-ack route delivery.

Negative acknowledgement depends on the active MQTT delivery. MQTT 5 QoS 1/2
publishes can carry a protocol-level rejection; QoS 0 and MQTT 3.1.1 deliveries
cannot. When the broker protocol cannot carry a rejection, `NackAsync` surfaces
that Pulse MQTT limitation to the trigger node as an acknowledgement failure.
