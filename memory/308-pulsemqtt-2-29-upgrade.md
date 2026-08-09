# 308 - Pulse MQTT 2.29 dependency upgrade

Date: 2026-08-09

Status: complete on local `main`; not published

## Decision

Upgrade the FluxFlow Pulse MQTT transport adapter from stable Pulse MQTT
`2.5.0` to stable `2.29.0`:

- `Pulse.Mqtt.Client` `2.5.0` -> `2.29.0`;
- `Pulse.Mqtt.Testing` `2.5.0` -> `2.29.0`.

Move the already-published FluxFlow adapter package from `4.0.0` to additive
patch `4.0.1`, making the dependency update consumable without changing public
API.

Keep the existing provider-neutral architecture. FluxFlow continues to use
Pulse's `RawMqttClient` behind `IMqttTransportFactory` and
`IMqttTransportSession`; it does not use Pulse's resilient hosted client,
dependency-injection lifecycle, routing, health, or durable stores.

## Findings

Pulse MQTT's post-2.0 semantic-version policy makes minor releases additive.
The current FluxFlow adapter compiled against `2.29.0` without a production
source migration. Existing raw-client, acknowledged-message, completion,
server-disconnect, topic-filter, and transport-factory APIs remained compatible.

This confirms the dependency upgrade does not change responsibility:

- FluxFlow owns reconnect and retry classification;
- FluxFlow owns desired subscriptions and restoration;
- FluxFlow owns workflow acknowledgement timing and message handoff;
- FluxFlow owns events, resource lifetime, and deterministic disposal;
- Pulse owns raw MQTT protocol and transport behavior inside its adapter.

## Changes

- Updated the two central Pulse MQTT package pins to `2.29.0`.
- Updated `FluxFlow.Components.Mqtt.PulseMqtt` from `4.0.0` to `4.0.1` because
  `4.0.0` is already published on NuGet.
- Updated the Pulse adapter package release notes.
- Documented the tested provider baseline in the adapter README.
- Added an Unreleased changelog entry.
- Recorded the executable scope in
  `goals/2026-08-09-pulsemqtt-2-29-upgrade/README.md`.

No other FluxFlow package version, public declaration, runtime source, MQTTnet
dependency, portable JSON contract, or application/component DSL changed.

## Verification

- Targeted restore resolved exact `Pulse.Mqtt.Client/2.29.0` and
  `Pulse.Mqtt.Testing/2.29.0` assets with zero errors or warnings.
- Release adapter build passed: 5 projects, 0 errors, 0 warnings.
- `FluxFlow.Components.Mqtt.PulseMqtt.Tests` passed: 6/6, 0 warnings.
- `FluxFlow.Components.Mqtt.Adapters.Tests` passed: 7/7, 0 warnings.
- `FluxFlow.Components.Mqtt.Tests` passed: 54/54, 0 warnings.
- Package release preflight passed for `components-mqtt-pulsemqtt` `4.0.1`.
- A temporary Release package `4.0.1` was created successfully for `net8.0`
  and `net10.0`. Its generated nuspec required `Pulse.Mqtt.Client` `2.29.0` in
  both target-framework groups and retained the neutral
  `FluxFlow.Components.Mqtt` dependency.
- The temporary package output was removed after inspection.
- Scoped repository hygiene and formatting checks completed after the final
  documentation update.

No full-solution test, publication, tag, feed write, release workflow, or
external deployment was performed.

## Follow-up

WebSocket transport selection remains a separate feature round. It should add
an explicit portable transport choice to the neutral FluxFlow MQTT broker
configuration and map that choice in provider adapters. This dependency-only
upgrade intentionally does not add `Pulse.Mqtt.Transport.WebSocket` or change
the current TCP/TLS configuration surface.
