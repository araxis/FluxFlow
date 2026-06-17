# FluxFlow.Components.Mqtt

MQTT component package for FluxFlow.

This package keeps protocol-specific nodes outside `FluxFlow.Engine` while
preserving the same runtime registration model.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `mqtt.connection` | resource | Owns the MQTT client lifecycle (profile + reconnect policy). Publish and subscribe nodes reference it by name. |
| `mqtt.publish` | `Input` -> `Result` | Publishes `MqttPublishRequest` values through a borrowed connection. |
| `mqtt.subscribe` | source -> `Output` | Emits `MqttReceivedMessage` values from a borrowed connection subscription. |

The package does not include a concrete network client. Applications provide an
adapter through `RegisterMqttComponents`.

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterMqttComponents((profile, cancellationToken) =>
        ValueTask.FromResult<IMqttClientAdapter>(
            new AppMqttClientAdapter(profile)));
```

Options are static node settings. Requests and messages are per-item data.

## Connection resource

The `mqtt.connection` node owns the client. It is a resource: `mqtt.publish`
and `mqtt.subscribe` do not open their own clients, they borrow the established
adapter from a connection by name. The connection carries the broker `profile`
and an optional `reconnect` policy.

```json
{
  "type": "mqtt.connection",
  "name": "broker",
  "profile": {
    "host": "broker.internal.example",
    "port": 1883,
    "clientId": "fluxflow",
    "useTls": false
  },
  "reconnect": {
    "enabled": true,
    "maxAttempts": 5,
    "initialDelayMilliseconds": 100,
    "maxDelayMilliseconds": 5000,
    "backoffMultiplier": 2,
    "useJitter": true
  }
}
```

`reconnect` values are advisory. The package validates them and passes them to
the adapter factory through `MqttClientFactoryContext.Reconnect`. The adapter
still owns connection state, retry loops, shared client behavior, and
broker-specific recovery.

`mqtt.publish` and `mqtt.subscribe` require a `connectionName` that names an
`mqtt.connection` resource. The reference is mandatory; there is no inline
broker configuration on publish or subscribe.

```json
{
  "type": "mqtt.publish",
  "name": "publisher",
  "connectionName": "broker",
  "defaultTopic": "devices/state"
}
```

```json
{
  "type": "mqtt.subscribe",
  "name": "subscriber",
  "connectionName": "broker",
  "topicFilter": "devices/+/state"
}
```

## Connecting (host-driven)

Connecting is an explicit host decision: there is no auto-connect or lazy
connect. `StartAsync` on the connection is a no-op. The host establishes and
tears down the client through `IMqttConnectionHandle`:

```csharp
await connection.ConnectAsync(cancellationToken);
// ... run the graph ...
await connection.DisconnectAsync(cancellationToken);
```

`ConnectAsync` is idempotent (a no-op when already connected) and single-flight
(a concurrent call awaits the in-flight connect). Publish and subscribe borrow
the established adapter and never connect or dispose it; a borrow only succeeds
while the connection is `Connected`.

`mqtt.publish` bounds each adapter publish with `publishTimeoutMilliseconds`
(default `30000`). A publish that exceeds the timeout is reported as a
per-message error and the node continues with later requests, so a hung
adapter cannot wedge the node.

Return `MqttClientLease.Owned(adapter)` from the factory when the connection
should dispose the adapter. Return `MqttClientLease.Shared(adapter)` when the
host owns a shared client lifetime.

Subscriptions return `IMqttSubscription`; once `SubscribeAsync` returns,
startup is considered successful. Each subscription should expose an independent
message stream so shared clients can safely serve multiple nodes.

## Runtime Timing

Use `MqttComponentOptions.UseClock(TimeProvider)` when tests or hosts need
deterministic package-owned timestamps. The package uses `System.TimeProvider`;
there is no bespoke MQTT clock interface.

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterMqttComponents(options => options
        .UseClientFactory(factory)
        .UseClock(TimeProvider.System));
```

`mqtt.publish` uses the configured clock for `MqttPublishResult.Timestamp`.
MQTT publish, subscribe, and connection health events also use that clock for
their `FlowEvent.Timestamp` values.

Incoming `MqttReceivedMessage.Timestamp` values stay adapter-owned because they
represent when the adapter observed the broker message. Adapter-provided
`MqttClientHealthEvent.Timestamp` values also stay adapter-owned, while the
emitted workflow event timestamp uses the configured package clock.

## Adapter Health

Adapters that also implement `IMqttClientHealthSource` can expose connection
health transitions. `mqtt.publish` and `mqtt.subscribe` forward those values as
diagnostics and events named `mqtt.connection.healthChanged`.

The package does not own reconnect policy. Adapters decide how to connect,
reconnect, and report state.

## Topic validation

Use `MqttTopicValidator.ValidatePublishTopic` and
`MqttTopicValidator.ValidateSubscriptionFilter` when projecting host settings or
building requests.

Publish topics must be present and cannot contain MQTT wildcards. Subscription
filters may use `+` as a complete level and `#` only as the final complete
level. Both helpers also reject null characters and oversized encoded topics.

## Design Metadata

This package exposes a package-owned `IComponentDesignMetadataProvider` for its
node types. Hosts can compose it through `ComponentDesignMetadataCatalog` to
populate palettes, editors, validation views, and documentation without
duplicating package descriptors.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
