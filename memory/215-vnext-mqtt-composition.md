# vNext MQTT Composition

Date: 2026-07-18

## Status

The twelfth bounded vNext milestone is implemented on local branch
`work/mqtt-composition-vnext`. No push, tag, publication, pull request, or
merge was performed.

This milestone migrates `FluxFlow.Components.Mqtt.Composition` to the canonical
two-section application model and completes the MQTT vertical slice from
transport-neutral contracts through concrete adapters, JSON composition,
stable runtime ports, Designer metadata, and package-only consumption.

## Canonical Resources

- `AddMqttCompositionResources(...)` recursively flattens canonical nested
  `Resources` and registers each resource by its full ordinal address.
- `mqtt.broker` owns the endpoint, transport TLS, and server settings. Multiple
  `mqtt.client` resources can share the same broker configuration while each
  receives an independent host-lifetime `IMqttClientController` and transport
  session.
- `mqtt.client` binds client identity, direct or referenced credentials,
  direct or referenced certificates, clean start, keepalive, exact-byte Last
  Will content, auto-connect, reconnect policy, and scalar-or-array named
  subscriptions.
- `mqtt.subscription` binds static named subscriptions using the JSON property
  `Qos`. `resilience.retry` binds fixed, linear, or exponential reconnect
  behavior. Host-provided keyed resource instances and defaults remain
  explicit DI boundaries.
- Direct client username/password values override referenced credential values.
  Inline password or certificate material requires approval through
  `IMqttInlineSecretPolicy`; a host can keep deployment secret material outside
  the document.
- Resource and nested-object properties are validated strictly. Missing or
  wrong-type references fail registration, and duplicate referenced
  subscription leaf names fail deterministically because the client-owned
  subscription dictionary uses those names as identities.

## Components And Ports

- `mqtt.control` consumes `FlowMessage<MqttClientRequest>` and emits
  `FlowMessage<MqttClientResult>` for connect, disconnect, status, publish,
  subscribe, and unsubscribe commands.
- `mqtt.publish` is a focused `MqttPublishMessage` input with the same
  `MqttClientResult` output convention.
- `mqtt.trigger` emits `MqttReceivedApplicationMessage` values and accepts
  Ack/Nak through payload-independent signal inputs. Workflow Ack/Nak remains
  separate from broker acknowledgement policy and uses trace identity.
- `mqtt.events` emits reliable `MqttClientEvent` lifecycle and subscription
  events. None of the four nodes exposes a universal Error or State port.
- All four factories resolve the same keyed host-lifetime controller for a
  client resource and start it idempotently. Options expose semantic request
  processing, result ordering, capacity, subscription, and acknowledgement
  settings rather than Dataflow implementation names.

## Signal And Stable Runtime Integration

- Composition port metadata now explicitly distinguishes `Message` and
  `Signal` kinds. Signal inputs use `object` metadata, accept any
  `FlowMessage<T>` payload, and do not claim completion ownership from an
  individual link.
- Canonical link compilation and validation allow typed outputs to target
  signal inputs while preserving normal typed compatibility checks for message
  ports.
- Engine stable input mailboxes now include bounded signal mailboxes with the
  same address, revision, status, rejection, and keyed direct-access behavior
  as typed inputs. Direct sends and compiled routes can deliver different
  payload types to one stable signal address.
- Designer owns additive message/signal port attributes so hosts can render
  trigger Ack/Nak separately without taking a runtime or adapter dependency.
- A full-suite failure exposed an older prepared-output race: activation could
  check staging before a source fault propagated through Dataflow. Prepared
  outputs now retain and inspect source completion directly; the regression
  waits for the source fault to become observable and passed 30/30 stress
  iterations.

## Metadata, Sample, And Documentation

- MQTT metadata now describes the four vNext nodes, fixed ports, signal kinds,
  semantic option editor hints, one required canonical client resource, and an
  optional canonical clock resource.
- Canonical host-owned resource key patterns use `Resources.{name}`. Release
  conventions accept this absolute resource namespace in addition to existing
  package-local patterns.
- `FluxFlow.MqttCompositionSample` now runs both configuration and fluent
  registration paths against the vNext publish contract and shared controller,
  producing two deterministic acknowledgement results in each path.
- Package READMEs, getting started material, sample index, package descriptions,
  release notes, and the top-level changelog describe the canonical resource
  and component boundary.

## Compatibility And Versioning

- `FluxFlow.Composition` moves from local `2.3.0` to `2.4.0` for additive
  signal metadata, signal runtime links, and canonical factory-context helpers.
- `FluxFlow.Engine` moves from local `2.3.0` to `2.4.0` for additive stable
  signal ports and direct access.
- `FluxFlow.Components.Designer` moves from `2.17.1` to `2.18.0` for additive
  port-kind metadata contracts.
- `FluxFlow.Components.Mqtt.Composition` moves from `1.5.0` to `2.0.0` because
  the old publisher/trigger-source resources and `Responses` port are replaced
  by the client-centered four-node model. Existing consumers can remain on the
  published 1.x line while migrating.
- The reviewed source-declaration baseline changes only for Composition,
  Engine, Designer, and MQTT Composition.
- SDK package compatibility passed for Composition `2.4.0` against an exact
  local `2.3.0` package, Engine `2.4.0` against local `2.3.0`, and Designer
  `2.18.0` against published `2.17.1`. MQTT Composition validation against
  published `1.5.0` reports exactly the intentional major removals:
  `MqttCompositionPortNames.Responses`,
  `MqttCompositionResourceNames.Publisher`, and
  `MqttCompositionResourceNames.TriggerSource` on both target frameworks.

## Verification

- Composition tests: 126 passed.
- Engine tests: 98 passed; the prepared-source fault regression additionally
  passed 30/30 isolated iterations.
- Composition Hosting tests: 38 passed.
- Designer tests: 98 passed.
- MQTT core tests: 82 passed.
- MQTT Composition tests: 8 passed.
- MqttNet adapter tests: 37 passed.
- PulseMqtt adapter tests: 24 passed.
- Release convention tests: 93 passed.
- The complete Release sweep passed 1,983 tests across 63 projects with zero
  warnings and no skipped tests.
- Controlled Debug and Release solution builds each covered 130 projects with
  zero warnings and zero errors.
- Release preflight passed for Composition `2.4.0`, Engine `2.4.0`, Designer
  `2.18.0`, and MQTT Composition `2.0.0`.
- A temporary source outside the repository was seeded with all 58 current
  manifest packages. Archive, symbol, net8 smoke, feed-style restore/load, and
  complete release dry-runs passed for all four changed packages.
- A stronger package-only net8 consumer restored Composition, Engine, Designer,
  and MQTT Composition from that source plus NuGet. It bound nested broker,
  client, and subscription resources; preserved a host-provided keyed
  controller; activated the publish factory; verified trigger Ack/Nak signal
  metadata; delivered two differently typed values through one stable signal
  address; and printed `MQTT_COMPOSITION_API_OK`.
- `graphify update . --force` refreshed the ignored local graph to 16,056 nodes
  and 25,192 edges. `graphify-out/` remains excluded from tracked repository
  state.

## Deferred Boundaries

- The canonical resource binder is package-local. Generic host orchestration
  that discovers explicit resource registration extensions and composes full
  provider snapshots remains part of later Hosting/Designer integration.
- Existing normal component families still use their current payload and result
  contracts. They must migrate one bounded family at a time to `FlowValue`,
  `FlowContent`, and normal result variants where applicable.
- Legacy MQTT core declarations remain temporarily in the 5.0 package so
  existing concrete adapter APIs continue to compile during coordinated vNext
  release preparation. Their final removal is a separate reviewed major gate.
- Supervision, polling/GetLatest, durable mailboxes, broker clustering, and
  automatic mapper insertion remain deferred.

## Next Gate

Begin the broader component-family migration with Mapping as the first bounded
family. Move dynamic mapping to `FlowValue` without repeated JSON serialization,
retain standalone node use, update its Composition adapter and Designer
metadata, and prove package compatibility/consumer behavior before selecting
the next family.
