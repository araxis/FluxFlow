# MQTT Clock Hardening

Date: 2026-06-02

## Goal

Make MQTT package-owned timestamps deterministic for tests, replay, and
dashboard projections without changing broker adapter contracts or taking
ownership of incoming broker message timestamps.

## Decision

Add `IMqttClock` to `FluxFlow.Components.Mqtt`.

The MQTT package owns this clock because `mqtt.publish` creates publish result
messages and both MQTT nodes emit workflow events. Host adapters still own
timestamps for incoming broker messages and adapter-originated health data.

`MqttClientFactoryContext` now carries the configured clock so adapter
factories can align package-facing behavior with the same time source when
needed.

## Implemented

- Added `IMqttClock` and `SystemMqttClock`.
- Added `MqttComponentOptions.UseClock(...)`.
- Threaded the configured clock through `MqttComponentModule` and
  `MqttClientFactoryContext`.
- Updated `mqtt.publish` to use the configured clock for
  `MqttPublishResult.Timestamp`.
- Updated MQTT publish, subscribe, and connection health workflow events to use
  the configured clock.
- Updated generated health-stream failure events to use the configured clock.
- Added focused tests for publish result timestamps, MQTT workflow event
  timestamps, and factory-context clock propagation.

## Version Plan

- `FluxFlow.Components.Mqtt` -> `0.4.0-alpha.1`

## Release

- Tag: `components-mqtt-v0.4.0-alpha.1`
- Release workflow: `26845161855`, succeeded.

## Verification

- `dotnet test tests\FluxFlow.Components.Mqtt.Tests\FluxFlow.Components.Mqtt.Tests.csproj -c Release --no-restore /nr:false`
- `dotnet build FluxFlow.sln -c Release --no-restore /nr:false`
- `dotnet test FluxFlow.sln -c Release --no-restore /nr:false`
- `dotnet pack src\FluxFlow.Components.Mqtt\FluxFlow.Components.Mqtt.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`
- Public package-feed restore/build smoke passed after feed propagation and a
  no-cache restore.
