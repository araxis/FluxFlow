# FluxFlow.Components.Mqtt.MqttNet

Concrete transport adapter for `FluxFlow.Components.Mqtt`.

The adapter maps the neutral broker configuration to MQTTnet TCP, TLS,
WebSocket, or secure WebSocket channel options. `WebSocketPath` defaults to
`/mqtt`, and WebSocket connections request the standard `mqtt` subprotocol.
No MQTTnet type enters portable JSON or the core authoring surface.

The package exposes `MqttNetTransportFactory`, which implements the neutral
`IMqttTransportFactory` boundary. The resulting transport session maps resolved
client configuration, exact `FlowContent` bytes, subscriptions, connection
events, transport failures, and broker acknowledgements.

The adapter does not own workflow policy. `MqttClientController` owns:

- logical client lifecycle
- auto-connect and reconnect policy
- desired subscription restoration
- trigger ownership claims
- command results
- workflow acknowledgement
- diagnostic events

## Composition

This adapter does not expose composition factories or depend on
`FluxFlow.Composition`. Register it with the host, then let
`FluxFlow.Components.Mqtt.Composition` resolve the transport factory for a
canonical `mqtt.client` resource.

### Registration

Register one host default:

```csharp
services.AddSingleton<IMqttTransportFactory>(
    new MqttNetTransportFactory());
```

Or select adapters per logical client using the full client resource address:

```csharp
services.AddKeyedSingleton<IMqttTransportFactory>(
    "Resources.Messaging.Client1",
    new MqttNetTransportFactory());
```

`FluxFlow.Components.Mqtt.Composition` resolves a keyed factory first and then
the unkeyed host default. The host or Composition package creates and owns the
controller.

## Boundary

- Broker endpoint, client identity, credentials, certificates, keepalive,
  clean-start, and Last Will arrive as `MqttClientConfiguration`.
- Publish payloads retain exact bytes and content metadata.
- Received provider messages become `MqttReceivedApplicationMessage` values.
- QoS delivery tokens remain provider details behind the transport session.
- Provider failures are classified through `MqttTransportException` so the
  core can construct stable normal results.
- The session does not implement reconnect policy or desired-state ownership.
- Browser applications use WebSocket/WSS. Native client-certificate and other
  host capability checks belong to the browser host, not this adapter.

Version 2 removes the previous convenience client, adapter-specific client
options, hosted lifecycle registration, publisher/trigger interfaces, and
health API. Use the core controller and this transport factory instead.
