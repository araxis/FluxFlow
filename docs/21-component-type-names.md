# Component Type Names

`Type` is a definition-level discriminator. On a component it selects a
registered operation; on a resource it selects a host-recognized resource
kind. It is not a runtime mode, lifetime setting, implementation class name, or
dependency-injection key.

Use lowercase `domain.operation` names for components and lowercase
`domain.kind` names for resources. Prefer a singular domain and a direct verb
for an operation: `data.map`, `json.validate`, `http.request`. Resource names
are nouns because they describe reusable configuration or host-owned state:
`mqtt.client`, `retry.policy`.

## Configuration Boundary

```json
{
  "Resources": {
    "Broker1": {
      "Type": "mqtt.broker",
      "Host": "broker.example.net",
      "Port": 443,
      "Transport": "WebSocket",
      "UseTls": true,
      "WebSocketPath": "/mqtt"
    },
    "Client1": {
      "Type": "mqtt.client",
      "Broker": "Resources.Broker1",
      "AutoConnect": true
    }
  },
  "Workflows": {
    "Telemetry": {
      "Map": {
        "Type": "data.map",
        "Expression": "payload"
      },
      "Publish": {
        "Type": "mqtt.publish",
        "Client": "Resources.Client1",
        "Input": "Map.Output"
      }
    }
  }
}
```

`Type` selects the component or resource factory. `Expression`, `Client`, and
`Input` configure that selected instance. Retry and lifetime settings are
configuration properties, not alternate type names. Canonical processing policy
uses an optional `processing.profile` resource rather than Dataflow-specific
component properties.

## Canonical Component Types

| Family | Types |
|--------|-------|
| Mapping and assertions | `data.map`, `data.assert` |
| Validation and payloads | `json.validate`, `payload.inspect` |
| JSON, text, and Base64 | `json.parse`, `json.stringify`, `text.encode`, `text.decode`, `base64.encode`, `base64.decode` |
| State and events | `state.reduce`, `event.project`, `event.expect` |
| Metrics and logging | `metric.count`, `metric.measure`, `metric.aggregate`, `log.write` |
| Routing | `flow.window`, `flow.correlate`, `flow.join` |
| Resilience | `flow.retry` |
| Sources | `source.items`, `source.sequence` |
| Timers | `timer.interval`, `timer.schedule`, `timer.delay`, `timer.throttle`, `timer.debounce` |
| Files | `file.read`, `file.write`, `directory.list`, `file.watch` |
| HTTP | `http.request` |
| Sessions | `session.record`, `session.replay`, `session.query` |
| Storage | `storage.put`, `storage.get`, `storage.query`, `storage.delete` |
| MQTT | `mqtt.command`, `mqtt.publish`, `mqtt.receive`, `mqtt.events` |

Control 5 and Routing 5 remove `flow.filter`, `flow.when`, `flow.switch`,
`flow.fork`, and `flow.merge`. Canonical definitions use conditional links and
ordinary fan-out/fan-in instead.

`flow.retry` is an executable workflow component. `retry.policy` is a reusable
resource containing policy settings. The removed `resilience.retry` name is
rejected with guidance to use `retry.policy`. These names do not describe the
same runtime surface.

## Canonical MQTT Resource Types

| Type | Meaning |
|------|---------|
| `mqtt.broker` | Shared broker endpoint and transport policy. |
| `mqtt.client` | One logical client identity, credentials, connection behavior, and desired subscriptions. |
| `mqtt.subscription` | Reusable MQTT subscription settings. |
| `retry.policy` | Reusable reconnect policy. |

An `mqtt.broker` combines `Transport` (`Tcp` or `WebSocket`) and `UseTls` to
describe TCP, TLS, `ws`, or `wss` without naming a concrete provider. Existing
definitions default to TCP when `Transport` is omitted. WebSocket definitions
may set `WebSocketPath`, which defaults to `/mqtt`.

Hosts may define additional resource types. Their names follow the same
`domain.kind` rule and remain host contracts rather than component-owned
resources.

## Removed Names

The registry performs exact canonical lookup. The following previous names are
rejected; update persisted definitions before loading them.

| Previous name | Canonical name |
|---------------|----------------|
| `flow.mapper` | `data.map` |
| `flow.assert` | `data.assert` |
| `json.schema-validator` | `json.validate` |
| `state.reducer` | `state.reduce` |
| `event.expectation` | `event.expect` |
| `event.projection` | `event.project` |
| `metrics.aggregate` | `metric.aggregate` |
| `flow.counter` | `metric.count` |
| `flow.logger` | `log.write` |
| `flow.metrics` | `metric.measure` |
| `flow.correlation` | `flow.correlate` |
| `source.generated` | `source.items` |
| `directory.enumerate` | `directory.list` |
| `http.client` | `http.request` |
| `session.recorder` | `session.record` |
| `mqtt.control` | `mqtt.command` |
| `mqtt.trigger` | `mqtt.receive` |
| `resilience.retry` | `retry.policy` resource |

There is no runtime rewrite or Designer fallback for these names. Treat this
table as a one-time migration map, persist the canonical result, and validate it
before deployment.

## Processing Resource Type

| Type | Meaning |
|------|---------|
| `processing.profile` | Reusable semantic `Mode`, `Order`, and `Buffer` policy mapped by the host. |

Components reference a profile through one flat `Processing` property. Defaults
require no profile. `boundedCapacity` remains an optional per-component
canonical runtime setting and is exposed as `BoundedCapacity` by C# component
builders. `MaxDegreeOfParallelism` and `EnsureOrdered` remain direct C#
compatibility options rather than normal canonical JSON or primary Designer
fields.
