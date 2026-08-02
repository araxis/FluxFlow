# vNext MQTT Core

Date: 2026-07-17

## Status

The tenth bounded vNext milestone is implemented on local branch
`work/mqtt-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone establishes the provider-neutral MQTT client model and core
nodes in `FluxFlow.Components.Mqtt` `5.0.0`. The existing 4.x declarations
remain available so the concrete adapters and current Composition package keep
building until their separately versioned migration.

## Ownership And Contracts

- `MqttBrokerConfiguration` describes an endpoint. `MqttClientConfiguration`
  describes one logical MQTT client with its own identity, credentials,
  certificates, Last Will, auto-connect mode, reconnect policy, and named
  subscriptions. Multiple logical clients can share one endpoint without
  sharing protocol state.
- `MqttClientController` owns one host-lifetime transport session for one
  logical client. It serializes lifecycle and subscription mutation while
  allowing configured concurrent command processing in the control node.
- Public transport abstractions isolate the core package from concrete MQTT
  libraries. Provider-specific connection objects and exceptions do not leak
  into core command, result, message, event, or configuration contracts.
- `MqttClientRequest` and `MqttClientResult` are discriminated JSON contracts.
  Expected failures are normal `MqttClientFailureResult` values containing a
  `FlowError`; the vNext nodes do not expose a universal error port.
- MQTT application payloads use `FlowContent`. Each component maps between the
  reusable content model and its own internal/provider types at its boundary.

## Nodes And Streams

- `MqttControlNode` accepts connect, disconnect, status, publish, subscribe,
  and unsubscribe requests and emits one result stream. Semantic scheduling
  options select sequential or concurrent processing and input- or
  completion-order results without exposing Dataflow option names.
- `MqttPublishOperationNode` provides the focused one-input/one-output publish
  shape while returning the same MQTT result hierarchy.
- `MqttSubscriptionTriggerNode` emits received application messages and accepts
  payload-independent Ack/Nak signals whose trace identity selects the pending
  delivery. Broker acknowledgement remains a separate configurable boundary.
- `MqttClientEventsNode` exposes lifecycle and reconnect events. Every vNext
  MQTT node separately exposes standard `FlowEvent` diagnostics; there is no
  misleading state port.
- No new node has an `Errors` property. Component failures remain observable
  through normal results and diagnostics without faulting the application.

## Subscriptions And Acknowledgements

- Trigger subscriptions accept one scalar or an array. A bare string is a
  named client subscription; an object is an inline subscription; mixed arrays
  are supported. JSON uses the flat `Subscription` property and `Qos` name.
- Static named subscriptions are client-owned. Inline subscriptions are
  trigger-owned and are removed when the trigger is disposed. A trigger may
  wait for a named subscription that is added later through a control command.
- A logical client rejects duplicate ownership of the same named or resolved
  topic-filter subscription. Different overlapping filters remain valid, and
  one broker message is deduplicated to one delivery per matching trigger.
- Workflow acknowledgement can be disabled or required. Broker acknowledgement
  can be automatic, after workflow handoff, or after Ack/Nak outcome. QoS 0
  does not require deferred acknowledgement capability.
- Ack/Nak uses first-outcome-wins semantics. Duplicate or late signals are
  diagnostics rather than component faults.

## Lifecycle And Recovery

- Auto-connect supports disabled and on-start modes. A failed on-start connect
  leaves the controller usable and schedules reconnect only for configured
  retry categories.
- Retry policy supports fixed, linear, and exponential delays, jitter, attempt
  and duration limits, stable-connection reset, and per-client override.
- Explicit disconnect suppresses automatic reconnect until an explicit connect
  or a new controller lifetime. Successful reconnect restores all desired
  named and inline subscriptions before the client is exposed as connected.
- Subscribe and unsubscribe commands are idempotent. Disconnected publish and
  subscription requests fail immediately as transient result values.
- Event subscribers and triggers are bounded and isolated. A closed subscriber
  or trigger cannot prevent sibling delivery or controller cleanup.

## Compatibility And Versioning

- `FluxFlow.Components.Mqtt` moves from `4.1.4` to `5.0.0` because the package
  gains the permanent vNext command/result, controller, transport, trigger,
  acknowledgement, and event contracts.
- The package now directly references `FluxFlow.Data` `1.0.0` and current
  `FluxFlow.Nodes` `2.1.0` while retaining the legacy RequestReply dependency
  during migration.
- The reviewed public source-declaration baseline changes only for the MQTT
  core package. SDK binary compatibility passed against the published `4.1.4`
  baseline because legacy declarations remain present.
- Concrete adapter and MQTT Composition package versions are unchanged in this
  milestone.

## Verification

- MQTT core tests: 78 passed, covering lifecycle idempotence, retry policy,
  subscription restoration and cleanup, trigger ownership/deduplication,
  Ack/Nak races, capability checks, event replay, ordering modes, and JSON
  discriminators/scalar-or-array subscription shapes.
- The complete Release sweep passed 1,963 tests across 63 projects with zero
  warnings, including the legacy MQTT core, MqttNet, PulseMqtt, adapter, and
  Composition suites.
- Controlled Debug and Release solution builds each covered 130 projects with
  zero warnings and zero errors. Formatting verification passed for the core
  package and tests.
- Release convention tests passed 93 tests with the reviewed MQTT public API
  baseline.
- Binary package compatibility passed for MQTT `5.0.0` against `4.1.4`, and
  release preflight passed.
- The initial dry-run packed and compiled the package but correctly exposed
  that `FluxFlow.Data 1.0.0` and `FluxFlow.Nodes 2.1.0` are not public yet. A
  fresh temporary source outside the repository was seeded with the complete
  dependency closure; archive, symbol, smoke, feed-style restore/load, and
  final dry-run checks then passed.
- A package-only net8 consumer restored only package references from that
  source plus NuGet, exercised controller startup and `MqttControlNode`, parsed
  scalar and mixed subscription JSON, and printed `MQTT_CORE_API_OK`.
- `graphify update . --force` refreshed the ignored local graph to 14,984
  nodes and 29,938 edges.

## Deferred Boundaries

- Concrete MqttNet and PulseMqtt implementations of the new transport SPI are
  deferred to the next bounded milestone. Existing adapter APIs continue to
  target the retained legacy contracts.
- Canonical `Resources`/`Workflows` binding, `mqtt.control`, `mqtt.publish`,
  `mqtt.trigger`, and `mqtt.events` registrations, Designer metadata, stable
  port integration, and resource validation remain a separate Composition
  migration.
- Cross-workflow supervision and polling remain deferred. Application-level
  failures continue to be represented as data and diagnostics rather than
  taking down the host.

## Next Gate

Implement concrete adapter conformance against the provider-neutral transport
SPI, one adapter at a time, with behavior shared by the same core controller
suite. Keep adapter packages responsible only for concrete client creation,
provider option mapping, protocol I/O, and lifecycle disposal; do not duplicate
controller, retry, subscription-ownership, or workflow acknowledgement policy.
