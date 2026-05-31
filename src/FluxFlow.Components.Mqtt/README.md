# FluxFlow.Components.Mqtt

MQTT component package for FluxFlow.

This package keeps protocol-specific nodes outside `FluxFlow.Engine` while
preserving the same runtime registration model.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `mqtt.publish` | `Input` -> `Result` | Publishes `MqttPublishRequest` values through an adapter. |
| `mqtt.subscribe` | source -> `Output` | Emits `MqttReceivedMessage` values from an adapter subscription. |

The package does not include a concrete network client. Applications provide an
adapter through `RegisterMqttComponents`.

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterMqttComponents((context, cancellationToken) =>
        ValueTask.FromResult(MqttClientLease.Owned(
            new AppMqttClientAdapter(context.Profile))));
```

Options are static node settings. Requests and messages are per-item data.

`MqttClientFactoryContext` includes the runtime node address, connection name,
and connection profile. Hosts can use `ConnectionName` to resolve app-owned
resources instead of placing all broker settings inline.

Return `MqttClientLease.Owned(adapter)` when the node should dispose the
adapter. Return `MqttClientLease.Shared(adapter)` when the host owns a shared
client lifetime.

Subscriptions return `IMqttSubscription`; once `SubscribeAsync` returns,
startup is considered successful. Each subscription should expose an independent
message stream so shared clients can safely serve multiple nodes.

## Topic validation

Use `MqttTopicValidator.ValidatePublishTopic` and
`MqttTopicValidator.ValidateSubscriptionFilter` when projecting host settings or
building requests.

Publish topics must be present and cannot contain MQTT wildcards. Subscription
filters may use `+` as a complete level and `#` only as the final complete
level. Both helpers also reject null characters and oversized encoded topics.
