# FluxFlow.Components.Mqtt.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone MQTT nodes
from `FluxFlow.Components.Mqtt`.

This package does not create MQTT clients, own broker settings, or choose a client
library. Concrete adapter packages or the host register keyed `IMqttPublisher` and
`IMqttTriggerSource` services. Composition definitions reference those keys as
resources.

## Registration

```csharp
services.AddKeyedSingleton<IMqttPublisher>("primary", publisher);
services.AddKeyedSingleton<IMqttTriggerSource>("primary", triggerSource);

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterMqttNodes());
```

## Node Types

| Type | Node | Required resource | Ports |
|------|------|-------------------|-------|
| `mqtt.publish` | `MqttPublishNode` | `publisher` | `Input`, `Output` |
| `mqtt.trigger` | `MqttTriggerNode` | `triggerSource` | `Output`, `Responses` |

`clock` is an optional keyed `TimeProvider` resource for deterministic timestamps
and trigger response timeout tests.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "commands": {
              "type": "mqtt.trigger",
              "resources": {
                "triggerSource": "primary"
              },
              "configuration": {
                "topicFilter": "commands/+",
                "mode": "RequestReply",
                "acknowledgement": "OnSuccessfulResponse"
              }
            },
            "publishResult": {
              "type": "mqtt.publish",
              "resources": {
                "publisher": "primary"
              },
              "configuration": {
                "publishTimeoutMilliseconds": 30000
              }
            }
          },
          "links": []
        }
      }
    }
  }
}
```

The composition package binds only node runtime options. Publish topics, payloads,
quality of service, retain flags, and MQTT protocol metadata still come from
`MqttPublishRequest` messages at runtime.

To publish a response to a received MQTT message, insert a transform node that
maps `MqttReceivedMessage` to `MqttPublishRequest` before linking into
`publishResult.Input`.

## Design Metadata

`MqttComponentDesignMetadataProvider` exposes neutral Designer metadata for
`mqtt.publish` and `mqtt.trigger` so hosts can build palettes, editors,
validation hints, or documentation without copying package descriptors. The
metadata describes the existing MQTT node option records, host-owned resources,
and fixed ports. `mqtt.publish` requires `publisher`; `mqtt.trigger` requires
`triggerSource`; both can reference an optional `clock`. Resource metadata is
descriptive only, so `IMqttPublisher`, `IMqttTriggerSource`, and optional keyed
`TimeProvider` clocks remain host-owned and are not modeled as editable node
options.
