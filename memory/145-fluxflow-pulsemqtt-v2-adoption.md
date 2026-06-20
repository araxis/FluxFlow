# 145 - FluxFlow Pulse MQTT v2 adoption

Date: 2026-06-20

Status: implemented on `work/mqtt-connection-pilot`; not merged or released.

## Decision

Move `FluxFlow.Components.Mqtt.PulseMqtt` from the upstream Pulse MQTT `1.1.0`
packages to the stable `2.0.0` packages:

- `Pulse.Mqtt.Client` `1.1.0` -> `2.0.0`
- `Pulse.Mqtt.Testing` `1.1.0` -> `2.0.0`

Use the stable `2.0.0` packages rather than the immediately newer
`2.1.0-preview.66` packages. The FluxFlow adapter package is preparing a stable
initial release and should not take a preview dependency unless a specific v2.1
feature is required.

## Code Impact

Pulse MQTT v2 split broker subscriptions from local routing. The FluxFlow
adapter already followed that ownership model:

- broker delivery is still changed through `ResilientMqttClient.SubscribeAsync`;
- local trigger delivery is still read through a Pulse route stream.

The source update was therefore limited to the API rename:

- old: `_client.Router.OpenStream(routeTemplate, routeOptions)`
- new: `_client.OpenRouteStream(routeTemplate, routeOptions)`

The old `MqttRouteOptions.SubscriptionQualityOfService` assignment was removed
because subscription quality-of-service now belongs only to `MqttTopicFilter`,
which the adapter already builds for `SubscribeAsync`.

## Verification

- Adapter build:
  `dotnet build src\Mqtt\FluxFlow.Components.Mqtt.PulseMqtt\FluxFlow.Components.Mqtt.PulseMqtt.csproj --configuration Release --nologo`
  passed for `net8.0` and `net10.0`.
- Focused Pulse MQTT adapter tests:
  `dotnet test tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed (`8`).
- Focused core MQTT tests:
  `dotnet test tests\FluxFlow.Components.Mqtt.Tests\FluxFlow.Components.Mqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed (`48`).
- Release convention tests:
  `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed (`33`).
- `graphify update . --force` refreshed local graph output after the code and
  memory update: 7950 nodes, 11971 edges, 753 communities. `graph.html` was
  skipped because the graph exceeds the local HTML visualization limit.
