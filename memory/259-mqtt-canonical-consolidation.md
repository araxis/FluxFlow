# MQTT Canonical Consolidation

Date: 2026-07-25

## Status

The MQTT family is consolidated on local branch
`work/canonical-vnext-cleanup`. No push, tag, package publication, pull request,
or merge was performed.

## Canonical Runtime

- `MqttClientController` remains the single public facade for one logical
  client and delegates to focused internal connection, command, validation,
  result, event, subscription, received-message, and broker-outcome
  collaborators.
- Independent clients can share one broker configuration without sharing
  sessions, identity, credentials, desired subscriptions, or reconnect state.
- Multiple command, publish, receive, and events components can share one
  host-owned controller.
- Exact MQTT payload bytes and content metadata use immutable `FlowContent`.
- Expected command and publish failures remain `MqttClientFailureResult`
  values on normal `Output`; caller cancellation remains cancellation.
- Named and inline subscriptions, reconnect restoration, exclusive effective
  trigger claims, overlapping-filter delivery, and payload-independent
  TraceId-matched Ack/Nak behavior remain covered by deterministic tests.

## Removed Parallel Surface

- Removed the 4.x publisher, trigger-source, subscription, received-context,
  health, unavailable-error, byte-array message, request/reply, legacy node,
  option, diagnostic-name, and numeric-error contracts.
- Removed the core dependency on `FluxFlow.Components.RequestReply`.
- Removed concrete adapter convenience clients, adapter-specific client
  options, hosted registration helpers, legacy subscriptions and received
  contexts, and the provider-store compatibility type.
- Both concrete adapters now expose only their provider transport factory and
  session implementation over the neutral MQTT 6 SPI.
- Shared adapter registration uses ordinary keyed `IMqttTransportFactory`
  services addressed by the full client resource address.

## Composition

- The catalog registers `mqtt.command`, `mqtt.publish`, `mqtt.receive`, and
  `mqtt.events`; old control/trigger names remain input aliases only.
- Resource registration is split into indexing, validation, conversion,
  binding, and registration modules. Component factories are isolated from
  registry declarations.
- Broker, client, subscription, retry, credentials, certificates, Last Will,
  scalar-or-array references, inline-secret policy, keyed transport factories,
  and optional clocks preserve their established behavior.
- Designer labels and component diagnostic names now use command/receive
  terminology consistently.
- Release metadata convention tests now follow delegated internal factory
  methods instead of requiring every factory body to remain in the registry
  extension file.

## Versions And Compatibility

- `FluxFlow.Components.Mqtt` is `6.0.0`.
- `FluxFlow.Components.Mqtt.Composition` is `3.0.0`.
- `FluxFlow.Components.Mqtt.MqttNet` is `2.0.0`.
- `FluxFlow.Components.Mqtt.PulseMqtt` is `3.0.0`.
- The reviewed source-declaration baseline changed only package indices 9
  through 12.
- SDK package validation against Core `5.0.0`, Composition `2.2.0`, MqttNet
  `1.2.0`, and PulseMqtt `2.1.0` passed for Composition and reported only the
  intentional major-version removals for Core and both concrete adapters. No
  compatibility suppressions were added.
- Release preflight and complete local-source dry-runs passed for all four
  packages.

## Verification

- MQTT Core: 48 passed, zero warnings.
- MQTT Composition: 9 passed, zero warnings.
- MqttNet: 8 passed, zero warnings.
- PulseMqtt: 6 passed, zero warnings.
- Shared adapter conformance: 7 passed, zero warnings.
- Composition: 109 passed, zero warnings.
- Composition.Hosting: 29 passed, zero warnings.
- Engine: 55 passed, zero warnings.
- Designer: 112 passed, zero warnings.
- Release: 99 passed, zero warnings.
- Controlled Debug and Release builds completed 129 projects with zero errors
  and zero warnings. Cold builds exceeded their command windows; immediate
  controlled reruns supplied the authoritative successful results.
- All 58 current packages were packed into a temporary source outside the
  repository.
- A fresh net8.0 consumer with 58 direct package references restored from that
  source plus the public feed and built in Release with warnings as errors.
- The MQTT composition sample produced identical configuration- and
  definition-driven outputs.

## Remaining Program Work

The final cleanup audit found that Routing still retains generic typed Window,
Correlation, and Join compatibility beside the canonical FlowValue/result
nodes. Consolidate that family in a separate bounded commit before recording
overall canonical cleanup completion.
