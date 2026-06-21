# 149 - FluxFlow Pulse MQTT manual acknowledgement adoption

Date: 2026-06-20

Status: implemented locally on `main`; not yet released as a FluxFlow package.

## Decision

Move `FluxFlow.Components.Mqtt.PulseMqtt` from upstream Pulse MQTT `2.0.0` to
stable `2.4.0` so the adapter can use Pulse acknowledged route streams.

Package references:

- `Pulse.Mqtt.Client` `2.0.0` -> `2.4.0`
- `Pulse.Mqtt.Testing` `2.0.0` -> `2.4.0`

The FluxFlow adapter package version is now `1.1.0` because behavior changed:
manual trigger acknowledgement modes are now supported instead of rejected.

## Code Impact

`PulseMqttClient.SubscribeAsync(...)` now chooses the Pulse route stream by
FluxFlow acknowledgement policy:

- `MqttTriggerAcknowledgement.None` uses `OpenRouteStream(...)`, preserving
  Pulse's managed acknowledgement after local delivery.
- `OnEmit` and `OnSuccessfulResponse` use `OpenAcknowledgedRouteStream(...)`.

Pulse acknowledged route streams are single-owner for each matching publish, so
overlapping manual-ack subscriptions on one `PulseMqttClient` should not be used
when every route needs a copy. Managed-ack route streams remain the better fit
for broadcast-style local route delivery.

`PulseMqttReceivedContext` now delegates:

- `AckAsync` -> Pulse `MqttAcknowledgedRoutedMessage.AcknowledgeAsync`
- `NackAsync` -> Pulse `MqttAcknowledgedRoutedMessage.RejectAsync`

Ack/nack calls are idempotent at the FluxFlow adapter context. Negative
acknowledgement still depends on protocol support: MQTT 5 QoS 1/2 can carry a
negative acknowledgement, while QoS 0 and MQTT 3.1.1 cannot. When Pulse reports
that rejection is unsupported, the exception is allowed to reach
`MqttTriggerNode`, which emits the existing acknowledgement-failed diagnostic.

## Upstream Release

The required upstream Pulse MQTT work is available as stable `v2.4.0`:

- Commit: `99963b4`
- Tag: `v2.4.0`
- Release workflow: `27880444942`
- GitHub release:
  `https://github.com/araxis/pulse-mqtt/releases/tag/v2.4.0`
- All ten `Pulse.Mqtt.*` packages, including `Pulse.Mqtt.Testing`, are indexed
  on NuGet as `2.4.0`.

## Verification

- `dotnet restore tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests.csproj --nologo`
  restored Pulse MQTT `2.4.0`.
- `dotnet build src\Mqtt\FluxFlow.Components.Mqtt.PulseMqtt\FluxFlow.Components.Mqtt.PulseMqtt.csproj --configuration Release --no-restore --nologo`
  passed for `net8.0` and `net10.0`.
- `dotnet test tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed: `9` tests.
- `dotnet test tests\FluxFlow.Components.Mqtt.Tests\FluxFlow.Components.Mqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed: `48` tests.
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed: `33` tests.
- `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\eng\package-release-preflight.ps1 -Package components-mqtt-pulsemqtt`
  passed and resolved `FluxFlow.Components.Mqtt.PulseMqtt` `1.1.0`.
- `dotnet pack src\Mqtt\FluxFlow.Components.Mqtt.PulseMqtt\FluxFlow.Components.Mqtt.PulseMqtt.csproj --configuration Release --no-build`
  produced `FluxFlow.Components.Mqtt.PulseMqtt.1.1.0.nupkg` and `.snupkg` in
  a temporary output folder.
- `graphify update . --force` refreshed local code graph output:
  `7921` nodes, `11917` edges, `750` communities. `graph.html` was skipped
  because the graph exceeds the local HTML visualization limit.

## Next

If the adapter package is to be consumed outside the repo, run the package
release preflight for `components-mqtt-pulsemqtt` and publish
`FluxFlow.Components.Mqtt.PulseMqtt` `1.1.0`.
