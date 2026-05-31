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
    .RegisterMqttComponents((connection, cancellationToken) =>
        ValueTask.FromResult<IMqttClientAdapter>(
            new AppMqttClientAdapter(connection)));
```

Options are static node settings. Requests and messages are per-item data.
