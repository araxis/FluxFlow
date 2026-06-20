# 144 - Pulse MQTT 2.0 route/subscription release

Date: 2026-06-20

Status: merged and released upstream.

## Scope

Upstream source: `D:\Projects\MqttNg`.

The Pulse MQTT client library was cleaned up for a v2 breaking release so broker
delivery and local message routing are separate concepts:

- `SubscribeAsync` and `UnsubscribeAsync` are the explicit broker-delivery APIs.
- Local routing now uses `RegisterRoute(...)`, `OpenRouteStream(...)`,
  `RegisterRequestHandler(...)`, and `RegisterRequestStreamHandler(...)`.
- Removed the implicit-subscribe route APIs: `OnAsync(...)`,
  `OpenStreamAsync(...)`, `OnRequestAsync(...)`, and
  `OnRequestStreamAsync(...)`.
- `MqttRouteOptions` is now local-route-only: capacity, overflow, and
  concurrency.
- MQTT 5 subscription options stay on `MqttTopicFilter`.
- `MqttRouteTemplate.ToTopicFilter(...)` and
  `MqttRouteBuilder.ToTopicFilter(...)` make the explicit subscribe step
  concise.
- Fluent route terminals are now synchronous local-route operations:
  `Handle(...)`, `Handle<T>(...)`, and `Stream()`.

This confirms the earlier FluxFlow adapter design direction: adapter packages
should subscribe through explicit broker APIs and then attach local route streams
without hiding subscription ownership.

## Release Record

- Branch: `work/route-subscription-contract`
- PR: `https://github.com/araxis/pulse-mqtt/pull/96`
- Merge commit: `dcca05e`
- Stable tag: `v2.0.0`
- GitHub release:
  `https://github.com/araxis/pulse-mqtt/releases/tag/v2.0.0`
- Post-release development commit on `main`: `347a78b`
- Current development version: `2.1.0`
- Preview published from `main`: `2.1.0-preview.66`

Stable `2.0.0` package publishing succeeded for all packages and NuGet
flat-container indexing was confirmed for:

- `Pulse.Mqtt.Core`
- `Pulse.Mqtt.Client`
- `Pulse.Mqtt.DependencyInjection`
- `Pulse.Mqtt.Serialization.Json`
- `Pulse.Mqtt.Serialization.MessagePack`
- `Pulse.Mqtt.Resilience.Polly`
- `Pulse.Mqtt.Storage.Sqlite`
- `Pulse.Mqtt.Transport.WebSocket`
- `Pulse.Mqtt.Testing`

The `2.1.0-preview.66` packages were pushed successfully by the release
workflow; NuGet indexing was still lagging during the immediate post-publish
check.

## Verification

- Local `dotnet build -c Release` passed before PR publication.
- Local release tests passed with broker-matrix and soak categories excluded:
  440 passed, 0 failed, 0 skipped.
- Docs build passed with `npm run docs:build`.
- PR checks passed: build, broker matrix, and build-and-test.
- Stable release workflow run `27872125048` passed and pushed all nine
  `2.0.0` packages.
- Main preview workflow run `27872310368` passed and pushed all nine
  `2.1.0-preview.66` packages.
- `graphify update . --force` refreshed local FluxFlow graph output after the
  memory update: 7944 nodes, 11965 edges, 756 communities. `graph.html` was
  skipped because the graph exceeds the local HTML visualization limit.

## FluxFlow Follow-up

Resolved on `work/mqtt-connection-pilot`: `FluxFlow.Components.Mqtt.PulseMqtt`
now targets `Pulse.Mqtt.Client` `2.0.0`, and its tests target
`Pulse.Mqtt.Testing` `2.0.0`. The adapter already modeled broker subscription
and local route stream ownership separately, so the source change was limited to
using the v2 `OpenRouteStream(...)` API.
