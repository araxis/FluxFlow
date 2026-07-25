# Canonical vNext Migration

This guide records intentional next-major removals and their canonical
replacements. It is updated as each cleanup ledger entry is completed.

## Composition 2.x To 3.0

Composition 3.0 removes the parallel persisted definition and runtime path:

- `CompositionDefinition`, its workflow/node/link/reference DTOs, and JSON helper
- `CompositionDefinitionBuilder`
- `CompositionConfigurationLoader`
- `CompositionValidator` and its diagnostics
- `CompositionRuntimeBuilder` and `CompositionBuildResult`
- legacy definition sources and reload planner contracts
- node-oriented `CompositionNodeFactoryContext` members

Use `ApplicationDefinition`, canonical links, application revision hosting, and
component-oriented factory contexts. `CompositionRuntime` remains only as the
small lifecycle owner for already-created code-first or Engine descriptors.

### JSON

Before:

```json
{
  "workflows": {
    "Orders": {
      "nodes": {
        "Source": {
          "type": "source.items",
          "configuration": {
            "items": ["alpha", "beta"]
          }
        },
        "Sink": {
          "type": "sample.sink",
          "resources": {
            "store": "Resources.Storage.Primary"
          }
        }
      },
      "links": [
        { "from": "Source.Output", "to": "Sink.Input" }
      ]
    }
  }
}
```

After:

```json
{
  "Resources": {
    "Storage": {
      "Primary": {
        "Type": "storage.memory"
      }
    }
  },
  "Workflows": {
    "Orders": {
      "Source": {
        "Type": "source.items",
        "items": ["alpha", "beta"]
      },
      "Sink": {
        "Type": "sample.sink",
        "store": "Resources.Storage.Primary",
        "Input": "Source.Output"
      }
    }
  }
}
```

Legacy resource slots were references to host-owned keyed services; migration
does not invent resource definitions. Add canonical `Resources` entries and DI
registrations according to the host's ownership model.

### Explicit Conversion

```csharp
using FluxFlow.Composition.Migration;
using FluxFlow.Composition.Model;

var migrator = new LegacyCompositionDefinitionMigrator();
var definition = migrator.Migrate(legacyJson);
var canonicalJson = ApplicationDefinitionJson.Serialize(definition);
```

The migrator also accepts UTF-8 JSON or an `IConfiguration` root/section. It is
strict: unknown properties, option/resource collisions, missing link endpoints,
and link/property collisions fail rather than producing a lossy application.
Persist canonical JSON and use normal canonical loading thereafter.

## Composition.Hosting 2.x To 3.0

Hosting 3.0 removes:

- `AddFluxFlowComposition(...)` and its builder
- `ICompositionRuntimeHost` and `CompositionRuntimeHost`
- legacy hosted-service options and exception contracts
- static/configuration Composition definition sources
- obsolete factory-context resource extension methods

Before:

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterAppComponents());
```

After:

```csharp
services
    .AddFluxFlowApplication(canonicalConfiguration)
    .UseRuntimeAssembler(runtime => runtime.RegisterNodes(registry =>
        registry.RegisterAppComponents()));
```

The canonical host keeps one active complete application definition, prepares
candidate revisions transactionally, preserves the active revision after a
rejection, and drains/disposes replaced revisions. Adapter packages remain the
owners of concrete clients, stores, clocks, credentials, and other resources.

## MQTT 4.x And 5.x To 6.0

MQTT 6.0 removes the parallel 4.x publisher, trigger-source, health,
byte-array message, request/reply, Errors-port, and concrete convenience-client
surfaces. The provider-neutral controller, exact-content contracts, transport
SPI, and four canonical components are now the only maintained MQTT path.

| Removed surface | Canonical replacement |
|---|---|
| `IMqttPublisher`, `MqttPublishNode`, `MqttPublishRequest`, `MqttPublishResult`, `MqttPublishOptions` | `IMqttClientController`, `MqttPublishOperationNode`, `MqttPublishMessage`, and `MqttClientResult` |
| `IMqttTriggerSource`, `IMqttSubscription`, `IMqttReceivedContext`, `MqttTriggerNode`, `MqttTriggerOptions` | Controller trigger registration, `MqttSubscriptionTriggerNode`, and `MqttReceivedApplicationMessage` |
| `MqttTriggerResponse` | Payload-independent Ack/Nak signals matched by `TraceId` |
| `IMqttClientHealthSource`, `MqttClientHealthEvent`, `MqttClientHealthState` | `MqttClientEventsNode` and `MqttClientEvent` variants |
| Byte-array MQTT payload properties | Immutable exact bytes and metadata in `FlowContent` |
| Legacy adapter clients and hosted registration helpers | `IMqttTransportFactory` registered explicitly, optionally keyed by the full client resource address |
| `Errors` output | `MqttClientFailureResult` on normal `Output`, routed by `IsError`, `Kind`, and `Error.Code` |

### C# Publish

Before:

```csharp
var node = new MqttPublishNode(publisher, new MqttPublishOptions());
await node.Input.SendAsync(FlowMessage.Create(new MqttPublishRequest
{
    Topic = "orders/accepted",
    Payload = payload,
    QualityOfService = MqttQualityOfService.AtLeastOnce
}));
```

After:

```csharp
var controller = new MqttClientController(configuration, transportFactory);
await controller.StartAsync(cancellationToken);

await using var node = new MqttPublishOperationNode(controller);
await node.Input.SendAsync(FlowMessage.Create(new MqttPublishMessage
{
    Topic = "orders/accepted",
    Content = FlowContent.FromBytes(payload, "application/json"),
    Qos = MqttQos.AtLeastOnce
}), cancellationToken);
```

Expected publish failures are `MqttClientFailureResult` values on
`node.Output`. Caller cancellation remains cancellation.

### Canonical Application JSON

Before, MQTT components depended on host-created publisher and trigger-source
keys inside legacy node/resource wrappers. After migration, broker and logical
client ownership are explicit resources and components use flat properties:

```json
{
  "Resources": {
    "Messaging": {
      "Broker1": {
        "Type": "mqtt.broker",
        "Host": "broker.internal",
        "Port": 8883,
        "UseTls": true
      },
      "Commands": {
        "Type": "mqtt.subscription",
        "TopicFilter": "commands/+",
        "Qos": "AtLeastOnce"
      },
      "Client1": {
        "Type": "mqtt.client",
        "Broker": "Resources.Messaging.Broker1",
        "ClientId": "orders-client",
        "Subscriptions": "Resources.Messaging.Commands",
        "AutoConnect": "OnStart"
      }
    }
  },
  "Workflows": {
    "Orders": {
      "Receive": {
        "Type": "mqtt.receive",
        "Client": "Resources.Messaging.Client1",
        "Subscription": "Commands",
        "Output": "Handle.Input"
      },
      "Handle": {
        "Type": "orders.handle"
      }
    }
  }
}
```

The input aliases `mqtt.control`, `mqtt.trigger`, and `resilience.retry` remain
accepted for stored-definition migration. Canonical registry enumeration,
Designer metadata, documentation, and new persisted definitions use
`mqtt.command`, `mqtt.receive`, and `retry.policy`.

Package transitions:

- `FluxFlow.Components.Mqtt` `5.0.0` to `6.0.0`
- `FluxFlow.Components.Mqtt.Composition` `2.2.0` to `3.0.0`
- `FluxFlow.Components.Mqtt.MqttNet` `1.2.0` to `2.0.0`
- `FluxFlow.Components.Mqtt.PulseMqtt` `2.1.0` to `3.0.0`

The concrete adapter packages now expose only their transport factories over
the MQTT 6 SPI. The core controller owns connection policy, reconnect, desired
subscriptions, trigger claims, acknowledgement coordination, command results,
and client events.

## Compatibility Report

These removals are intentional source and binary breaks against published
`FluxFlow.Composition` 2.7.0 and `FluxFlow.Composition.Hosting` 2.3.0. No shim
recreates the removed parallel architecture. Package validation should report
the corresponding removals until the 3.0 baselines are published; review those
reports as expected breaking-change evidence rather than suppressing them.

MQTT package validation uses published baselines `FluxFlow.Components.Mqtt`
5.0.0, `FluxFlow.Components.Mqtt.Composition` 2.2.0,
`FluxFlow.Components.Mqtt.MqttNet` 1.2.0, and
`FluxFlow.Components.Mqtt.PulseMqtt` 2.1.0. The resulting removed-declaration
diagnostics are intentional major-version evidence. Do not suppress them or
reintroduce the parallel APIs as compatibility shims.
