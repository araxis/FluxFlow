# 146 - Pulse MQTT OnAsync convenience

Date: 2026-06-20

Status: implemented, merged, tagged, and released upstream.

## Decision

Restore a small endpoint-style `OnAsync(...)` convenience API in Pulse MQTT
without undoing the v2 broker-subscription/local-routing split.

The API is intentionally a shorthand for:

- register a local route handler;
- subscribe the route's broker topic filter;
- return an async-disposable handle that unregisters the local route and
  unsubscribes the broker filter.

This keeps the one-line routing ergonomics that were useful in `1.1.0` while
preserving the cleaner v2 contract: advanced subscription ownership, queue
capacity, overflow, and concurrency still use explicit `SubscribeAsync(...)`
plus `Route(...).With...` / local route APIs.

## Shape

Added upstream:

- `MqttSubscribedRoute : IAsyncDisposable`, exposing the subscribed
  `MqttTopicFilter`.
- `ResilientMqttClient.OnAsync(...)` overloads for raw and typed route
  handlers, with optional explicit maximum QoS and cancellation overloads.

The convenience uses string route templates only. It deliberately does not add a
second advanced routing model around `MqttRouteOptions`; those options stay
local-route-only.

Failure behavior:

- local route registration happens before broker subscription to avoid an
  immediate-delivery race;
- failed subscribe acknowledgements throw `MqttException`;
- subscribe failure cleanup disposes the local route and attempts a best-effort
  broker unsubscribe.

## FluxFlow Impact

No FluxFlow package dependency changed in this step.

`FluxFlow.Components.Mqtt.PulseMqtt` still targets Pulse MQTT `2.0.0` until
there is a deliberate adapter adoption step. The existing FluxFlow adapter still
correctly uses the explicit v2 model, so the new `OnAsync(...)` helper is an
ergonomic upstream addition rather than an adapter blocker.

## Release Record

- Implementation branch: `feature/route-convenience`
- Merge PR: `https://github.com/araxis/pulse-mqtt/pull/97`
- Merge commit on upstream `main`: `2c8c5b6`
- Stable tag: `v2.1.0`
- Stable release workflow run: `27873206048`
- Stable package version: `2.1.0`
- Post-release branch: `chore/next-development-cycle`
- Post-release PR: `https://github.com/araxis/pulse-mqtt/pull/98`
- Current upstream `main` commit after the post-release bump: `19bcb64`
- Current upstream development version: `2.2.0`
- First post-release preview package version: `2.2.0-preview.69`

The stable `2.1.0` package release and the `2.2.0-preview.69` package publish
both completed successfully for all nine upstream packages:

- `Pulse.Mqtt.Core`
- `Pulse.Mqtt.Client`
- `Pulse.Mqtt.DependencyInjection`
- `Pulse.Mqtt.Serialization.Json`
- `Pulse.Mqtt.Serialization.MessagePack`
- `Pulse.Mqtt.Resilience.Polly`
- `Pulse.Mqtt.Storage.Sqlite`
- `Pulse.Mqtt.Transport.WebSocket`
- `Pulse.Mqtt.Testing`

## Verification

In `D:\Projects\MqttNg`:

- `dotnet build -c Release` passed with 0 warnings and 0 errors.
- `dotnet test -c Release --no-build --logger "console;verbosity=minimal" --filter "Category!=BrokerMatrix&Category!=Soak"` passed:
  442 tests, 0 failed, 0 skipped.
- `npm run docs:build` passed from `D:\Projects\MqttNg\docs`.
- PR checks passed for the implementation PR and the post-release PR.
- Stable workflow run `27873206048` passed and attached all nine `2.1.0`
  package artifacts to the `v2.1.0` release.
- Public feed flat-container checks returned `200` for all nine `2.1.0`
  packages.
- Post-release workflow run `27873384358` passed and public feed
  flat-container checks returned `200` for all nine `2.2.0-preview.69`
  packages.
- `graphify update . --force` refreshed FluxFlow's local `graphify-out/`
  output after recording this memory entry.
