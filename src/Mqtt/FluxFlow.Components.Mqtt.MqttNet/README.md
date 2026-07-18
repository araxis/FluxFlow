# FluxFlow.Components.Mqtt.MqttNet

MQTTnet-backed adapter for `FluxFlow.Components.Mqtt`.

The current integration point is `MqttNetTransportFactory`. It implements the
provider-neutral MQTT transport SPI and creates one non-resilient provider
session for each logical `MqttClientController` connection. This package maps:

- resolved broker, credential, certificate, and Last Will configuration;
- exact `FlowContent` bytes and MQTT message metadata;
- subscribe and unsubscribe operations; and
- deferred broker Ack/Nak completion.

`FluxFlow.Components.Mqtt` owns logical-client lifetime, auto-connect,
reconnect, desired subscriptions, trigger claims, delivery policy, result
values, and diagnostics. The adapter does not run a second reconnect or
subscription policy loop.

## Usage

```csharp
var configuration = new MqttClientConfiguration
{
    Name = "primary",
    ClientId = "fluxflow-worker",
    Broker = new MqttBrokerConfiguration
    {
        Host = "localhost",
        Port = 1883
    },
    AutoConnect = MqttAutoConnectMode.OnStart
};

await using var client = new MqttClientController(
    configuration,
    new MqttNetTransportFactory());

await client.StartAsync();
```

Publish and Last Will content must carry original bytes in `FlowContent`; a
workflow mapper or serializer performs any JSON, text, or domain conversion
before the MQTT boundary. QoS 1/2 deliveries are deferred until the core
controller resolves the configured workflow acknowledgement policy.

## Legacy Client API

`MqttNetClient`, `IMqttPublisher`, `IMqttTriggerSource`, and
`IMqttClientHealthSource` remain available while the current MQTT Composition
adapter migrates. That legacy client retains its existing reconnect,
subscription-stream, and health behavior. New host integration should use
`MqttClientController` with `MqttNetTransportFactory`.

Mapper helpers reject null text before encoding MQTT credentials, correlation
data, and user properties so malformed adapter input fails with clear argument
names.
User-property maps are optional: null maps are treated as empty, blank property
names are ignored, and named properties with null values are rejected with a
clear `value` argument error.
`MqttNetClientOptions.UserProperties` also snapshots assigned dictionaries, so
caller-owned maps cannot alter CONNECT user properties after options creation.
`MqttNetLastWillOptions.Payload` snapshots the assigned byte array, and publish
and Last Will payloads are copied before concrete client handoff so caller-owned buffers
cannot alter queued client-library messages.

## Legacy Dependency Injection

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
The registration helpers reject null service collections, blank keys, null
direct options, null options factories, and null options factory results before
creating the keyed client session.
Keyed DI helper names are trimmed before registration, so configuration-bound
client names with surrounding whitespace resolve to the same logical client.

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

## Composition

This package does not expose `FluxFlow.Composition` node factories. It owns the
MQTTnet-backed client session and DI registration only.

The current `FluxFlow.Components.Mqtt.Composition` package still consumes the
legacy `IMqttPublisher`, `IMqttTriggerSource`, and optional
`IMqttClientHealthSource` resources. Canonical Composition binding to
`MqttClientController` is a separate migration milestone.

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
