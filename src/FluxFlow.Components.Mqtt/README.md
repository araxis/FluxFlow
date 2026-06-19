# FluxFlow.Components.Mqtt

Standalone MQTT nodes for FluxFlow, built on the [FluxFlow.Nodes](../FluxFlow.Nodes)
kit. No engine, registry, or runtime: `new` a connection handle and the publish /
subscribe nodes, `LinkTo` their broadcast ports, and run.

The package does not include a concrete network client. Applications supply an
`IMqttClientFactory` (and the `IMqttClientAdapter` it produces) for the connection to
establish.

## Nodes

| Node | Shape | Purpose |
|------|-------|---------|
| `MqttConnectionNode` | resource handle, `Events` out | Owns the MQTT client lifecycle (profile + reconnect policy). Publish and subscribe borrow the established adapter by reference. |
| `MqttPublishNode` | `FlowNode<MqttPublishRequest, MqttPublishResult>` | Publishes a request through the borrowed adapter and emits a result carrying the same correlation id. |
| `MqttSubscribeNode` | `FlowSource<MqttReceivedMessage>` | Opens a subscription on the borrowed adapter and emits each received message. |

Publish and subscribe are ordinary kit nodes: a bounded `Input` (publish), broadcast
`Output`, `Errors`, and `Events` ports. The connection is a resource, not a dataflow
node — it has no data ports, only a broadcast `Events` stream that reports connection
health.

## Connection (host-driven)

`MqttConnectionNode` owns the single client lease, connection epoch, and health
monitor. Connecting is an explicit host decision: there is no auto-connect or lazy
connect. The host establishes and tears down the client through
`IMqttConnectionHandle`:

```csharp
var connection = new MqttConnectionNode(
    connectionName: "broker",
    profile: new MqttConnectionProfile { Host = "broker.internal.example", Port = 1883 },
    reconnect: new MqttReconnectPolicy { Enabled = true, MaxAttempts = 5 },
    clientFactory: appClientFactory);

await connection.ConnectAsync(cancellationToken);
// ... run the graph ...
await connection.DisconnectAsync(cancellationToken);
await connection.DisposeAsync();
```

`ConnectAsync` is idempotent (a no-op when already connected) and single-flight (a
concurrent call awaits the in-flight connect rather than starting a second).
`DisconnectAsync`/`DisposeAsync` cancel an in-flight connect and never leak a freshly
built lease, even when a teardown wins the race mid-`CreateAsync`. Publish and
subscribe borrow the established adapter via `TryGetAdapter` and never connect or
dispose it; a borrow only succeeds while the connection is `Connected`.

Return `MqttClientLease.Owned(adapter)` from the factory when the connection should
dispose the adapter; return `MqttClientLease.Shared(adapter)` when the host owns a
shared client lifetime.

## Publish

```csharp
var publish = new MqttPublishNode(
    connection,
    new MqttPublishOptions { DefaultTopic = "devices/state", QualityOfService = MqttQualityOfService.AtLeastOnce });

publish.Output.LinkTo(resultSink);
await publish.Input.SendAsync(FlowMessage.Create(new MqttPublishRequest
{
    Topic = "devices/temperature",
    Payload = payloadBytes,
    CorrelationId = "abc"
}));
```

The result carries the inbound `FlowMessage` correlation id forward. When no client is
established the request reports `MqttErrorCodes.PublishNotConnected` on `Errors` (it is
not thrown) and the node continues with later requests. Each adapter publish is bounded
by `publishTimeoutMilliseconds` (default `30000`), so a hung adapter cannot wedge the
node.

## Subscribe

```csharp
var subscribe = new MqttSubscribeNode(
    connection,
    new MqttSubscriptionOptions { TopicFilter = "devices/+/state" });

subscribe.Output.LinkTo(messageSink);
await subscribe.StartAsync();
```

The source waits for the connection to become `Connected`, opens an `IMqttSubscription`,
and emits a `FlowMessage<MqttReceivedMessage>` for each message — flowing the
adapter-supplied correlation id when present. It (re)subscribes on each new connection
lease, deduped per connection epoch, so a within-lease `Reconnecting -> Connected` blip
does not resubscribe. The subscription (never the adapter) is disposed when the
connection drops or the node stops.

## Connection health

Adapters that also implement `IMqttClientHealthSource` can expose connection health
transitions. `MqttConnectionNode` forwards those on its `Events` port as
`FlowEvent`s named `mqtt.connection.healthChanged`, with the health state mapped to a
`FlowEventLevel` and the health attributes carried through.

## Runtime timing

Pass a `TimeProvider` to any node when tests or hosts need deterministic package-owned
timestamps (the publish result timestamp and the `FlowEvent.Timestamp` values).
Incoming `MqttReceivedMessage.Timestamp` and adapter-provided
`MqttClientHealthEvent.Timestamp` values stay adapter-owned.

## Topic validation

Use `MqttTopicValidator.ValidatePublishTopic` and
`MqttTopicValidator.ValidateSubscriptionFilter` when projecting host settings or
building requests. Publish topics must be present and cannot contain MQTT wildcards.
Subscription filters may use `+` as a complete level and `#` only as the final complete
level. Both helpers reject null characters and oversized encoded topics.
