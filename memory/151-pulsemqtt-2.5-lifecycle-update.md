# 151 - Pulse MQTT 2.5 lifecycle update

Date: 2026-06-21

Status: implemented locally on `main`; not yet released.

## Decision

Update the FluxFlow Pulse MQTT adapter to the latest stable upstream Pulse MQTT
line without changing FluxFlow's public runtime contracts.

MQTTnet was checked and remains current at `5.1.0.1559`, so no MQTTnet package
change is needed.

## Changes

- `Pulse.Mqtt.Client` moved from `2.4.0` to `2.5.0`.
- `Pulse.Mqtt.Testing` moved from `2.4.0` to `2.5.0`.
- `PulseMqttClient` now calls the upstream Pulse MQTT `ConnectAsync` and
  `DisconnectAsync` lifecycle APIs internally.
- FluxFlow keeps its adapter-level `StartAsync` / `StopAsync` helpers because
  they describe optional hosted lifecycle behavior in this package.
- Package release notes and the changelog now describe the Pulse MQTT `2.5.0`
  dependency.

## Verification

- `dotnet restore tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests.csproj --nologo`
  passed.
- `dotnet build src\Mqtt\FluxFlow.Components.Mqtt.PulseMqtt\FluxFlow.Components.Mqtt.PulseMqtt.csproj --configuration Release --no-restore --nologo`
  passed.
- `dotnet test tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed: `12` tests.
- `dotnet test tests\FluxFlow.Components.Mqtt.Tests\FluxFlow.Components.Mqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed: `48` tests.
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed: `33` tests.
- `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\eng\package-release-preflight.ps1 -Package components-mqtt-pulsemqtt`
  passed and resolved `FluxFlow.Components.Mqtt.PulseMqtt` `1.1.0`.
- `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\eng\package-release-dry-run.ps1 -Package components-mqtt-pulsemqtt -Version 1.1.0`
  passed with `DRY_RUN_OK=FluxFlow.Components.Mqtt.PulseMqtt`.
- `graphify update . --force` refreshed local code graph output: `7995` nodes,
  `11998` edges, and `755` communities. `graph.html` was skipped because the
  graph exceeds the local HTML visualization limit.

## Next

Publish the MQTTnet and Pulse MQTT adapter `1.1.0` updates together.
