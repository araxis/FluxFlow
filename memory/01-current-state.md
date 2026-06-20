# Current State

Date: 2026-06-20

## Repository

- `D:\Projects\FluxFlow` has `main` matching `origin/main`; active local work is
  on `work/mqtt-connection-pilot`.
- Private remote: `https://github.com/araxis/FluxFlow`.
- `graphify-out/` is local-only and excluded through `.git/info/exclude`; it is
  not part of the tracked repository state.
- Active pilot branch: `work/mqtt-connection-pilot` simplifies
  `FluxFlow.Components.Mqtt` so publish/trigger nodes depend on
  `IMqttPublisher` / `IMqttTriggerSource`, optional health uses
  `IMqttClientHealthSource`, and the package no longer includes a connection
  helper, adapter composition interface, or MQTT-specific request/reply helper
  folder. Trigger request/reply now runs through `MqttTriggerNode.Responses`
  with `MqttTriggerResponse` and shares pending response correlation/timeout
  mechanics through `FluxFlow.Components.RequestReply.CorrelatedRequestTracker`.
  Publish protocol metadata is grouped under `MqttPublishRequest.Properties`,
  publish topics are explicit per `MqttPublishRequest.Topic`, publish
  quality-of-service and retain semantics are owned by `MqttPublishRequest`, and
  workflow correlation stays on `FlowMessage.CorrelationId`. Static MQTT
  publish options now only describe timeout and bounded capacity; static trigger
  options still own subscription quality-of-service and acknowledgement mode.
  Adapter-owned client health uses the `mqtt.client.healthChanged` event name.
  `MqttEventNames` is the
  MQTT package's single name surface for emitted `FlowEvent` values. The
  current core MQTT package project lives under
  `src/Mqtt/FluxFlow.Components.Mqtt` so future MQTT-related adapter packages
  can sit beside it. The first concrete adapter package is now
  `FluxFlow.Components.Mqtt.MqttNet` under
  `src/Mqtt/FluxFlow.Components.Mqtt.MqttNet`; its `MqttNetClient` explicitly
  connects/disconnects, implements `IMqttPublisher`, `IMqttTriggerSource`, and
  `IMqttClientHealthSource`, owns MQTTnet client creation, Last Will setup,
  reconnect/resubscribe behavior, and maps MQTTnet acknowledgements through
  `IMqttReceivedContext`. The second concrete adapter package is now
  `FluxFlow.Components.Mqtt.PulseMqtt` under
  `src/Mqtt/FluxFlow.Components.Mqtt.PulseMqtt`; its `PulseMqttClient` wraps
  Pulse `ResilientMqttClient`, supports TCP/TLS or injected Pulse transports,
  exposes `StartAsync`/`StopAsync` plus connected-waiting `ConnectAsync`,
  maps publish/trigger/health contracts, preserves strict disconnected publish
  behavior by default, and rejects manual broker acknowledgement modes because
  Pulse route streams manage acknowledgement internally. It now targets the
  stable upstream Pulse MQTT `2.0.0` packages and uses explicit broker
  `SubscribeAsync` plus local `OpenRouteStream` routing. See
  `141-mqtt-connection-simplification-pilot.md`,
  `142-mqttnet-adapter-package.md`, `143-pulsemqtt-adapter-package.md`, and
  `144-pulsemqtt-2.0-route-subscription-release.md`.
- Upstream Pulse MQTT source at `D:\Projects\MqttNg` has a merged v2 route and
  subscription cleanup. PR #96 split broker subscribe/unsubscribe from local
  route registration, tagged `v2.0.0`, published all nine stable packages to
  NuGet, then opened the `2.1.0` development cycle. PR #97 restored a minimal
  endpoint-style `OnAsync(...)` convenience that subscribes a route filter,
  registers a local handler, and returns an async-disposable route handle;
  `v2.1.0` is tagged and all nine `2.1.0` packages are indexed on the public
  feed. PR #99 added explicit route-template `SubscribeAsync(...)` extension
  overloads for parsed `MqttRouteTemplate` values, preserving the broker/local
  routing split without hidden string-template detection. It is tagged as
  `v2.2.0`; release workflow run `27875265109` passed, and all nine stable
  `2.2.0` packages indexed on NuGet. PR #100 opened the `2.3.0` development
  cycle on `main`; workflow run `27875467096` published all nine
  `2.3.0-preview.72` packages and they are indexed. PR #101 added
  `Pulse.Mqtt.Storage.LiteDB` with `LiteDbMessageStore`,
  `LiteDbSessionStore`, package/docs/release workflow wiring, and focused
  tests; it is tagged as `v2.3.0`, release workflow run `27876350812` passed,
  and all ten stable `2.3.0` packages indexed on NuGet. PR #102 opened the
  `2.4.0` development cycle; workflow run `27876562110` passed on rerun and all
  ten `2.4.0-preview.75` packages indexed. FluxFlow still targets stable Pulse
  MQTT `2.0.0` until a separate adapter adoption step.

## FluxFlow solution

- Solution: `FluxFlow.sln`.
- Target frameworks: `net8.0` and `net10.0`.
- The current mainline is the standalone-node architecture:
  - `FluxFlow.Nodes` `1.0.0`: shared node kit.
  - `FluxFlow.Mapping` `1.0.0`: extracted mapping/expression abstractions.
  - `FluxFlow.Engine` `2.0.0`: optional composition runtime.
  - `FluxFlow.Components.RequestReply` `1.0.0`.
  - `FluxFlow.Components.Http.AspNetCore` `1.0.0`.
  - Engine-free dataflow component packages are on the `3.0.0` line.
- Infrastructure packages that were not part of the standalone-node major line
  keep their existing stable versions.

## Verification

- `dotnet test FluxFlow.sln --configuration Release` passed on 2026-06-20.
- A no-build Release verification with TRX aggregation passed with 742 tests:
  742 passed, 0 failed, 0 skipped.
- On `work/mqtt-connection-pilot`,
  `dotnet test .\FluxFlow.sln --configuration Release --no-restore --verbosity quiet --nologo`
  passed after the MQTT trigger request/reply, shared-tracker,
  topic-filter-validation, explicit publish-topic, publish-properties cleanup,
  and MQTT review cleanup.
- Focused RequestReply Release tests passed after adding
  `CorrelatedRequestTracker`: 15 passed, 0 failed, 0 skipped.
- Focused MQTT Release tests passed after the MQTT review cleanup:
  48 passed, 0 failed, 0 skipped.
- Focused MQTT Release tests passed after moving the core MQTT project under
  `src/Mqtt/FluxFlow.Components.Mqtt`: 48 passed, 0 failed, 0 skipped. The
  first run required a restore because the previous assets file referenced the
  old project path.
- Release convention tests passed after the MQTT layout move:
  33 passed, 0 failed, 0 skipped.
- Full solution Release tests passed after the MQTT layout move with:
  `dotnet test .\FluxFlow.sln --configuration Release --no-restore --verbosity quiet --nologo`.
- `graphify update . --force` refreshed `graphify-out/` after adding the
  MQTTnet adapter package and memory updates: 7783 nodes, 11712 edges,
  740 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- Added `FluxFlow.Components.Mqtt.MqttNet` and focused adapter tests on
  `work/mqtt-connection-pilot`. Verification passed:
  - `dotnet build src\Mqtt\FluxFlow.Components.Mqtt.MqttNet\FluxFlow.Components.Mqtt.MqttNet.csproj --configuration Release --no-restore --nologo`
  - `dotnet test tests\FluxFlow.Components.Mqtt.Tests\FluxFlow.Components.Mqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo` (`48` passed)
  - `dotnet test tests\FluxFlow.Components.Mqtt.MqttNet.Tests\FluxFlow.Components.Mqtt.MqttNet.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo` (`19` passed)
  - `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo` (`33` passed)
  - `dotnet test .\FluxFlow.sln --configuration Release --no-restore --verbosity quiet --nologo` passed after rerunning one transient existing Nodes test.
- Added `FluxFlow.Components.Mqtt.PulseMqtt` and focused adapter tests on
  `work/mqtt-connection-pilot`. Verification passed:
  - `dotnet build src\Mqtt\FluxFlow.Components.Mqtt.PulseMqtt\FluxFlow.Components.Mqtt.PulseMqtt.csproj --configuration Release --no-restore --nologo`
  - `dotnet test tests\FluxFlow.Components.Mqtt.Tests\FluxFlow.Components.Mqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo` (`48` passed)
  - `dotnet test tests\FluxFlow.Components.Mqtt.MqttNet.Tests\FluxFlow.Components.Mqtt.MqttNet.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo` (`19` passed)
  - `dotnet test tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo` (`8` passed)
  - `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo` (`33` passed)
  - `dotnet test .\FluxFlow.sln --configuration Release --no-restore --verbosity quiet --nologo` passed.
- `graphify update . --force` refreshed `graphify-out/` after adding the
  Pulse MQTT adapter package and memory updates: 7938 nodes, 11960 edges,
  759 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- Upstream Pulse MQTT v2.0 release work in `D:\Projects\MqttNg` passed local
  Release build/tests/docs, PR checks, stable release workflow run
  `27872125048`, NuGet flat-container indexing for all nine `2.0.0` packages,
  and post-release preview workflow run `27872310368` for
  `2.1.0-preview.66`.
- FluxFlow Pulse MQTT adapter adoption of upstream `2.0.0` passed:
  `dotnet build src\Mqtt\FluxFlow.Components.Mqtt.PulseMqtt\FluxFlow.Components.Mqtt.PulseMqtt.csproj --configuration Release --nologo`,
  `dotnet test tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  (`8` passed),
  `dotnet test tests\FluxFlow.Components.Mqtt.Tests\FluxFlow.Components.Mqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  (`48` passed), and
  `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  (`33` passed).
- `graphify update . --force` refreshed `graphify-out/` after adopting Pulse
  MQTT `2.0.0` in the FluxFlow adapter: 7950 nodes, 11971 edges,
  753 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- `graphify update . --force` refreshed `graphify-out/` after recording the
  upstream Pulse MQTT v2.0 release memory: 7944 nodes, 11965 edges,
  756 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- Upstream Pulse MQTT `2.1.0` restored the minimal `OnAsync(...)` route
  convenience in `D:\Projects\MqttNg`. Verification passed with a clean Release
  build, 442 non-soak/non-broker-matrix tests, and the VitePress docs build
  from `docs/`. PR #97 merged, tag `v2.1.0` was pushed, release workflow run
  `27873206048` passed, all nine `2.1.0` packages indexed on the public feed,
  PR #98 opened `2.2.0`, and workflow run `27873384358` published
  `2.2.0-preview.69` with all nine preview packages indexed.
- `graphify update . --force` refreshed `graphify-out/` after recording the
  upstream Pulse MQTT `OnAsync(...)` memory note.
- Upstream Pulse MQTT local `feature/route-template-subscribe` work added
  route-template `SubscribeAsync(...)` extension overloads. Verification passed
  with the client build, client tests (`89`), full Release build, broad
  non-soak/non-broker-matrix tests (`442`), and VitePress docs build.
- `graphify update . --force` refreshed `graphify-out/` after recording the
  upstream Pulse MQTT `2.2.0` stable release and `2.3.0-preview.72` publish:
  7962 nodes, 11983 edges, 753 communities. `graph.html` was skipped because
  the graph exceeds the local HTML visualization limit.
- Upstream Pulse MQTT `Pulse.Mqtt.Storage.LiteDB` work shipped as stable
  `2.3.0`. Verification passed with the LiteDB package build, LiteDB tests
  (`21`), full Release build, broad non-soak/non-broker tests (`463`), package
  creation for ten packages including `Pulse.Mqtt.Storage.LiteDB.2.3.0.nupkg`,
  VitePress docs build, PR #101 checks, release workflow run `27876350812`, and
  NuGet flat-container indexing for all ten stable packages. PR #102 then
  opened `2.4.0`; workflow run `27876562110` published
  `2.4.0-preview.75` for all ten packages after rerunning one existing chaos
  integration test flake.
- MQTT publish contract cleanup on `work/mqtt-connection-pilot` removed
  quality-of-service and retain defaults from `MqttPublishOptions`; those
  values now live only on `MqttPublishRequest`. Focused verification passed:
  core MQTT tests (`48`), MQTTnet adapter tests (`19`), Pulse MQTT adapter tests
  (`8`), and release convention tests (`33`).
- MQTT pilot release prep selects `FluxFlow.Components.RequestReply` `1.1.0`,
  `FluxFlow.Components.Mqtt` `4.0.0`, and initial
  `FluxFlow.Components.Mqtt.MqttNet` / `FluxFlow.Components.Mqtt.PulseMqtt`
  `1.0.0` packages. Release preflight and fast package dry-runs passed for all
  four packages; full solution Release tests also passed.
- `graphify update . --force` refreshed `graphify-out/` after recording the
  upstream Pulse MQTT LiteDB storage package memory: 7966 nodes, 11987 edges,
  755 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- `graphify update . --force` refreshed `graphify-out/` after recording the
  upstream Pulse MQTT `2.3.0` stable release and `2.4.0-preview.75` publish:
  7967 nodes, 11988 edges, 762 communities. `graph.html` was skipped because
  the graph exceeds the local HTML visualization limit.
- `graphify update . --force` refreshed `graphify-out/` after removing
  quality-of-service and retain from `MqttPublishOptions`: 7966 nodes,
  11986 edges, 764 communities. `graph.html` was skipped because the graph
  exceeds the local HTML visualization limit.
- `graphify update . --force` refreshed `graphify-out/` after MQTT pilot release
  prep and version bumps: 7968 nodes, 11988 edges, 756 communities.
  `graph.html` was skipped because the graph exceeds the local HTML
  visualization limit.
