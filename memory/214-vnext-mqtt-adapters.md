# vNext MQTT Transport Adapters

Date: 2026-07-17

## Status

The eleventh bounded vNext milestone is implemented on local branch
`work/mqtt-adapters-vnext`. No push, tag, publication, pull request, or merge
was performed.

This milestone implements the provider-neutral MQTT transport SPI in both
concrete adapter packages. `FluxFlow.Components.Mqtt.MqttNet` is now `1.2.0`
and `FluxFlow.Components.Mqtt.PulseMqtt` is now `2.1.0`. Existing adapter APIs
remain available while hosts migrate to the vNext controller and transport
factory model.

## Adapter Boundary

- Each package exposes an `IMqttTransportFactory` implementation and keeps its
  provider client, protocol options, acknowledgements, and exceptions behind
  an internal transport session.
- The core `MqttClientController` remains the only owner of logical-client
  policy: auto-connect, reconnect and retry, desired subscriptions, trigger
  claims, workflow acknowledgement, ordering, and application diagnostics.
- Transport sessions are intentionally non-resilient. The Pulse-backed session
  creates a fresh raw provider client for each connection lifetime instead of
  layering provider reconnect policy under the core controller.
- Broker endpoints, credentials, TLS certificates, Last Will, QoS, subscription
  flags, exact application bytes, content type, and content encoding are mapped
  at the concrete provider boundary. MQTT publication rejects `FlowContent`
  that has no original byte representation instead of choosing an implicit
  serialization.
- Provider failures map to `MqttTransportException` with stable transient or
  non-transient classification. Provider-specific exception types do not cross
  into the core command/result contracts.

## Delivery And Acknowledgement

- Both adapters provide bounded lifecycle/message channels and one-shot
  deferred broker acknowledgement tokens for QoS deliveries.
- Overlapping trigger matches now share one core acknowledgement coordinator.
  The broker receives one final outcome after every participant contributes;
  Nak takes precedence over timeout, which takes precedence over Ack.
- Automatic broker acknowledgement contributes after successful queue handoff,
  while after-handoff and after-outcome participants complete through the
  trigger node. Closed registrations contribute Nak. QoS 0 never invokes the
  transport acknowledgement API.
- Connect authentication/protocol failures and direct transport misuse retain
  deterministic failure classification. Canceled disconnect attempts leave a
  still-live transport session usable.

## Compatibility And Versioning

- MqttNet moves from `1.1.8` to `1.2.0`; PulseMqtt moves from `2.0.8` to
  `2.1.0`. The minor versions reflect the new public transport factories.
- Package release notes, READMEs, descriptions, changelog entries, and reviewed
  source-declaration baselines now describe the thin transport role and legacy
  transition surface.
- SDK package validation passed against `1.1.8` and `2.0.8` respectively.

## Verification

- MQTT core tests: 82 passed.
- MqttNet adapter tests: 37 passed.
- PulseMqtt adapter tests: 24 passed.
- Shared concrete-adapter conformance tests: 7 passed. The common suite covers
  lifecycle, exact payload and metadata delivery, subscriptions, deferred Ack
  idempotence, disconnect/reconnect, cancellation, and content validation.
- Existing MQTT Composition tests: 10 passed.
- Release convention tests: 93 passed.
- The complete Release sweep passed 1,977 tests across 63 projects with zero
  warnings. Controlled Debug and Release solution builds each covered 130
  projects with zero warnings and zero errors.
- Binary compatibility, package release preflight, and complete local-source
  release dry-runs passed for both adapter packages. The local source was
  created outside the repository and included the current vNext dependency
  closure.
- A temporary package-only net8 consumer restored both adapter packages from
  the isolated source, built with zero warnings, and printed
  `MQTT_ADAPTER_API_OK:MqttNetTransportFactory:PulseMqttTransportFactory`.
- `graphify update . --force` refreshed the ignored local graph to 15,218
  nodes and 30,545 edges.

## Deferred Boundaries

- Canonical `Resources`/`Workflows` binding and registration for
  `mqtt.control`, `mqtt.publish`, `mqtt.trigger`, and `mqtt.events` remain the
  next bounded milestone.
- That Composition pass owns keyed broker/client/controller resources,
  component option binding, fixed port metadata, Designer hints, definition
  validation, and stable-port integration. It must consume the core contracts
  and concrete transport factories without moving connection or retry policy
  into node factories.
- Legacy MQTT Composition factories remain unchanged in this milestone.

## Next Gate

Migrate `FluxFlow.Components.Mqtt.Composition` to the canonical MQTT model.
Prove flat resource and workflow JSON, host-lifetime controller sharing, all
four node types, resource-address validation, direct stable-port access, and
package-only consumption before beginning the broader component-family
migration.
