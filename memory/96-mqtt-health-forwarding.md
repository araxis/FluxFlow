# MQTT Health Forwarding

Date: 2026-06-02

`FluxFlow.Components.Mqtt` `0.3.0-alpha.1` adds optional adapter health
forwarding.

## Decision

The MQTT package still does not own a concrete client or reconnect policy.
Reconnect behavior belongs to the host-provided adapter. The package now offers
an optional `IMqttClientHealthSource` contract so adapters can report connection
state changes without changing the core publish/subscribe adapter contract.

## Runtime Shape

- `IMqttClientHealthSource.Health` exposes `MqttClientHealthEvent` values.
- `mqtt.publish` starts a health monitor when its adapter implements the
  optional contract.
- `mqtt.subscribe` starts the same monitor after subscription startup succeeds.
- Health entries are forwarded as diagnostics and events named
  `mqtt.connection.healthChanged`.
- Health stream failures are reported as health diagnostics/events and do not
  fault the MQTT node.

## Boundary

Adapters decide how to connect, reconnect, share clients, and emit health
states. The package only projects those states into the workflow diagnostic and
event channels.

## Verification

Completed verification:

- `dotnet test tests\FluxFlow.Components.Mqtt.Tests\FluxFlow.Components.Mqtt.Tests.csproj -c Release --no-restore`
- `dotnet build FluxFlow.sln -c Release --no-restore`
- `dotnet test FluxFlow.sln -c Release --no-restore`
- `dotnet pack src\FluxFlow.Components.Mqtt\FluxFlow.Components.Mqtt.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`
- GitHub release workflow `26829389666`: passed.
- Branch CI workflow `26829370331`: passed.
- Fresh public-feed restore/build smoke: passed on attempt 11.

Release tag:

`components-mqtt-v0.3.0-alpha.1`
