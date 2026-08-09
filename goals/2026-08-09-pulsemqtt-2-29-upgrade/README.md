# Goal: Upgrade the FluxFlow Pulse MQTT adapter to Pulse MQTT 2.29

Date: 2026-08-09

Status: completed successfully

## Objective

Upgrade FluxFlow's Pulse MQTT provider integration from the currently pinned
stable Pulse MQTT `2.5.0` packages to the current stable `2.29.0` packages
without changing FluxFlow's public MQTT contracts, lifecycle ownership,
portable definitions, or provider-neutral behavior.

The result must keep FluxFlow as the single owner of MQTT connection policy and
workflow behavior while using Pulse only for the raw protocol and transport
session implementation.

## Required changes

1. Update the centrally managed versions of:
   - `Pulse.Mqtt.Client` from `2.5.0` to `2.29.0`;
   - `Pulse.Mqtt.Testing` from `2.5.0` to `2.29.0`.
2. Move `FluxFlow.Components.Mqtt.PulseMqtt` from the already-published
   `4.0.0` package to the additive patch version `4.0.1`, so package consumers
   can receive the new dependency baseline.
3. Restore the affected projects from the configured NuGet sources and verify
   that the exact stable `2.29.0` assets resolve.
4. Compile the existing `FluxFlow.Components.Mqtt.PulseMqtt` adapter against
   the new dependency.
5. Apply only source migrations required by real Pulse API or behavior changes.
   Do not redesign the adapter speculatively.
6. Preserve the raw-client integration. The adapter must continue using
   Pulse's `RawMqttClient`; it must not register or construct Pulse's hosted
   resilient client.
7. Update package release notes, the repository changelog, current
   documentation, and repository memory so the dependency baseline and
   ownership decision are explicit.

## Preserved behavior

- `MqttClientController` owns start, stop, reconnect, retry classification,
  desired-subscription restoration, events, and disposal.
- FluxFlow owns workflow acknowledgement timing and delivery handoff.
- The Pulse transport session remains non-resilient and provider-specific only
  behind `IMqttTransportFactory` and `IMqttTransportSession`.
- Exact payload bytes, MQTT properties, subscriptions, Last Will, credentials,
  certificates, protocol failures, and broker acknowledgement mapping remain
  unchanged unless a verified upstream correction requires a focused update.
- Cancellation and deterministic disposal remain part of the adapter contract.
- No Pulse type enters portable JSON, component contracts, application
  definitions, typed ports, the MQTT core package, or consumer-facing workflow
  APIs.

## Explicit exclusions

- Do not add WebSocket transport selection to `MqttBrokerConfiguration` in this
  round.
- Do not add `Pulse.Mqtt.Transport.WebSocket`, QUIC, Pulse dependency-injection,
  Pulse health checks, Pulse routing, or Pulse durable stores.
- Do not replace FluxFlow reconnect or subscription ownership with Pulse's
  resilient-client layer.
- Do not change the MQTTnet adapter or its package version.
- Do not change any other FluxFlow package version or public API for this
  dependency update.
- Do not publish packages, create tags, dispatch release workflows, or write to
  a package feed.

## Verification plan

Use the repository's existing xUnit/Shouldly tests; add a new test only if the
upgrade exposes an uncovered behavioral contract.

Required gates:

1. Restore the Pulse adapter test project with zero restore errors or warnings.
2. Build `FluxFlow.Components.Mqtt.PulseMqtt` in Release with zero errors and
   warnings.
3. Run `FluxFlow.Components.Mqtt.PulseMqtt.Tests` in Release.
4. Run `FluxFlow.Components.Mqtt.Adapters.Tests` in Release so Pulse and
   MQTTnet still satisfy the same neutral conformance contract.
5. Run `FluxFlow.Components.Mqtt.Tests` in Release to protect controller,
   reconnect, subscription, acknowledgement, and ownership semantics.
6. Run package release preflight for `components-mqtt-pulsemqtt` `4.0.1`.
7. Pack `FluxFlow.Components.Mqtt.PulseMqtt` `4.0.1` into a temporary isolated
   output and verify its generated dependency metadata requires Pulse MQTT
   `2.29.0`.
8. Run scoped formatting/whitespace checks and `git diff --check`.

## Documentation and memory

- Add an Unreleased changelog entry describing the dependency update and the
  unchanged ownership boundary.
- Update the Pulse adapter README with the tested provider baseline.
- Add a new numbered memory entry containing the exact changes, verification
  results, and remaining WebSocket follow-up.
- Add the memory entry to `memory/00-index.md` and append a concise result to
  `memory/07-progress-log.md`.

## Completion criteria

The goal is complete when the exact stable Pulse MQTT `2.29.0` client/testing
packages are restored, the adapter and all three focused MQTT suites pass with
zero warnings, package `4.0.1` passes preflight, the packed adapter declares the
expected dependency, all scoped hygiene checks pass, documentation and memory
are current, and no excluded runtime or public-surface change has been
introduced.
