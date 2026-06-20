# 147 - Pulse MQTT route-template SubscribeAsync helper

Date: 2026-06-20

Status: merged upstream, tagged as Pulse MQTT `v2.2.0`, published to NuGet,
and followed by the `2.3.0` development-cycle preview.

## Decision

Add explicit route-template subscription ergonomics to Pulse MQTT without hidden
string-template detection.

The supported call shape is:

```csharp
await client.SubscribeAsync(
    MqttRouteTemplate.Parse("sensors/{device}/temp"),
    MqttQualityOfService.AtLeastOnce,
    token);
```

This keeps `SubscribeAsync` as the broker-delivery operation while letting a
caller pass the already-parsed route template directly. Internally the helper
uses `template.ToTopicFilter(maximumQualityOfService)` and delegates to the
existing raw filter subscription path.

The implementation is extension-based instead of new instance overloads. The
existing instance method has an optional cancellation token, and adding instance
overloads with the same name violates the public API analyzer's optional
parameter compatibility rule. Extension overloads preserve the call-site syntax
without changing the existing instance method shape.

Advanced MQTT 5 subscription flags still use explicit `ToTopicFilter(...)` so
there is no second options model.

## Verification

In `D:\Projects\MqttNg`:

- `dotnet build src\Pulse.Mqtt.Client\Pulse.Mqtt.Client.csproj --configuration Release --no-restore --nologo` passed.
- `dotnet test tests\Pulse.Mqtt.Client.Tests\Pulse.Mqtt.Client.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo` passed (`89`).
- `dotnet build -c Release` passed with 0 warnings and 0 errors.
- `dotnet test -c Release --no-build --logger "console;verbosity=minimal" --filter "Category!=BrokerMatrix&Category!=Soak"` passed:
  442 tests, 0 failed, 0 skipped.
- `npm run docs:build` passed from `D:\Projects\MqttNg\docs`.

## Release

- PR #99 (`https://github.com/araxis/pulse-mqtt/pull/99`) merged the helper.
- Tag `v2.2.0` points at merge commit
  `e57565cbdee091aea0db0c89cc81388802fe0748`.
- Release workflow run `27875265109` passed and created
  `https://github.com/araxis/pulse-mqtt/releases/tag/v2.2.0`.
- All nine stable `2.2.0` packages indexed on NuGet:
  `Pulse.Mqtt.Core`, `Pulse.Mqtt.Client`,
  `Pulse.Mqtt.DependencyInjection`, `Pulse.Mqtt.Serialization.Json`,
  `Pulse.Mqtt.Serialization.MessagePack`, `Pulse.Mqtt.Resilience.Polly`,
  `Pulse.Mqtt.Storage.Sqlite`, `Pulse.Mqtt.Transport.WebSocket`, and
  `Pulse.Mqtt.Testing`.
- The PR broker matrix failed once on `HiveMqCompatibilityTests.Shared_subscription`
  due to an `OperationCanceledException` timeout; rerunning the failed job
  passed before merge.
- PR #100 opened the next development cycle with `2.3.0` on `main` and merged
  as commit `c5d00af5c821847d8e9d3c84788db6f48d3b99af`.
- Release workflow run `27875467096` passed on `main` and published all nine
  `2.3.0-preview.72` packages, which later indexed successfully on NuGet.
