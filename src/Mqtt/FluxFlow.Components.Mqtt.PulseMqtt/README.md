# FluxFlow.Components.Mqtt.PulseMqtt

Concrete transport adapter for `FluxFlow.Components.Mqtt`.

The current tested provider baseline is Pulse MQTT `2.29.0`.

The adapter maps the neutral broker transport directly:

| FluxFlow broker configuration | Pulse transport |
| --- | --- |
| `Tcp`, `UseTls = false` | `TcpTransportFactory` |
| `Tcp`, `UseTls = true` | TLS `TcpTransportFactory` |
| `WebSocket`, `UseTls = false` | `WebSocketTransportFactory` with `ws` |
| `WebSocket`, `UseTls = true` | `WebSocketTransportFactory` with `wss` |

WebSocket connections use `WebSocketPath` and the standard `mqtt`
subprotocol. FluxFlow still creates Pulse's `RawMqttClient`; Pulse's resilient
or hosted client is not used, so reconnect and subscription ownership stay in
`MqttClientController`.

The package exposes `PulseMqttTransportFactory`, which implements the neutral
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
    new PulseMqttTransportFactory());
```

Or select adapters per logical client using the full client resource address:

```csharp
services.AddKeyedSingleton<IMqttTransportFactory>(
    "Resources.Messaging.Client1",
    new PulseMqttTransportFactory());
```

An existing provider transport factory may be supplied to
`PulseMqttTransportFactory` when the host owns provider transport setup.
`FluxFlow.Components.Mqtt.Composition` resolves a keyed factory first and then
the unkeyed host default.

## Boundary

- Broker endpoint, client identity, credentials, certificates, keepalive,
  clean-start, and Last Will arrive as `MqttClientConfiguration`.
- Publish payloads retain exact bytes and content metadata.
- Received provider messages become `MqttReceivedApplicationMessage` values.
- QoS delivery tokens remain provider details behind the transport session.
- Provider failures are classified through `MqttTransportException` so the
  core can construct stable normal results.
- The session does not implement reconnect policy, durable workflow mailboxes,
  or desired-state ownership.
- Browser applications use WebSocket/WSS. Native client-certificate and other
  host capability checks belong to the browser host, not this adapter.

Version 3 removes the previous convenience client, adapter-specific client and
store options, hosted lifecycle registration, publisher/trigger interfaces,
and health API. Use the core controller and this transport factory instead.
