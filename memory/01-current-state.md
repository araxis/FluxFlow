# Current State

Date: 2026-06-30

## Repository

- `D:\Projects\FluxFlow` is currently on local branch
  `work/designer-value-type-hint-contract`.
- The tracked worktree is clean as of the 2026-06-30 memory refresh.
- `graphify-out/` is local-only and excluded through `.git/info/exclude`; it is
  not part of the tracked repository state.
- Current architecture direction: standalone nodes are the default,
  `FluxFlow.Composition` is the optional standalone composition layer, component
  `.Composition` packages own factory registration and optional Designer
  metadata, and `FluxFlow.Engine` remains optional advanced runtime
  infrastructure.
- Composition adapters now exist for the normal standalone component families:
  HTTP, Mapping, Control, Assertions, Validation, Timers, Sources, Routing,
  Serialization, Payloads, Observability, Projections, Metrics, Expectations,
  FileSystem, State, Storage, Sessions, and MQTT. Request/reply is intentionally
  skipped as a normal component-family adapter; Journal remains support-only.
- Designer has been decoupled from engine identifiers and now owns its own
  design-time value types. Package-owned metadata providers are in place across
  composition packages, with shared metadata helpers and stronger validation.
- The active narrow track is richer Designer metadata hints. Mapping was the
  pilot; Control, Assertions, State, Observability, and Validation followed.
  Routing is a reasonable later package-family candidate, but it should be
  planned separately.
- See `155-composition-and-designer-progress.md` for the current summary and
  verification notes.
- MQTT connection pilot PR #24 is merged and released. It simplifies
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
  behavior by default, uses Pulse managed acknowledgement for
  `MqttTriggerAcknowledgement.None`, and maps manual trigger acknowledgement
  modes to Pulse `OpenAcknowledgedRouteStream(...)` contexts. It now targets the
  stable upstream Pulse MQTT `2.5.0` packages and uses explicit broker
  `SubscribeAsync` plus local route streams. See
  `141-mqtt-connection-simplification-pilot.md`,
  `142-mqttnet-adapter-package.md`, `143-pulsemqtt-adapter-package.md`,
  `149-pulsemqtt-manual-ack-adoption.md`, and
  `151-pulsemqtt-2.5-lifecycle-update.md`.
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
  ten `2.4.0-preview.75` packages indexed. Commit `99963b4` then added manual
  inbound broker acknowledgement support, tag `v2.4.0` was pushed, release
  workflow run `27880444942` passed, GitHub release
  `https://github.com/araxis/pulse-mqtt/releases/tag/v2.4.0` was created, and
  all ten stable `2.4.0` packages indexed on NuGet. Pulse MQTT `2.5.0` is the
  current stable line consumed by the FluxFlow Pulse adapter source; FluxFlow
  now uses the upstream MQTT-named `ConnectAsync` / `DisconnectAsync` lifecycle
  APIs internally while keeping its adapter-level `StartAsync` / `StopAsync`
  host lifecycle helpers.
- MQTT pilot release set is published and indexed on NuGet:
  `FluxFlow.Components.RequestReply` `1.1.0`,
  `FluxFlow.Components.Mqtt` `4.0.0`,
  `FluxFlow.Components.Mqtt.MqttNet` `1.0.0`, and
  `FluxFlow.Components.Mqtt.PulseMqtt` `1.0.0`. PR #24 merged with squash
  commit `118a06de613a9ebdfd47e9e06b7c6761161a4d37`; release workflow runs
  `27877804072`, `27877844606`, `27877876917`, and `27877966707` completed
  successfully. The package feed was explicitly verified after publication.
  Current source keeps core `FluxFlow.Components.Mqtt` pure at `4.0.0` with no
  client capability descriptor or cross-adapter registration package.
  `FluxFlow.Components.Mqtt.MqttNet` is bumped to `1.1.0` for adapter-local DI
  registration and optional hosted connect/disconnect lifetime.
  `FluxFlow.Components.Mqtt.PulseMqtt` is bumped to `1.1.0` for Pulse MQTT
  `2.5.0` manual acknowledgement support, adapter-local DI registration,
  optional hosted startup, and optional Pulse message/session store hooks. These
  FluxFlow package changes have not yet been published.
  `FluxFlow.Components.Mqtt.Composition` is now added as an optional
  composition adapter package for `mqtt.publish` and `mqtt.trigger` node
  factories over keyed `IMqttPublisher` / `IMqttTriggerSource` resources; core
  MQTT remains pure and broker/client ownership stays in adapters or hosts. See
  `150-mqtt-di-and-adapter-owned-features.md` and
  `154-mqtt-composition-adapter.md`.

## FluxFlow solution

- Solution: `FluxFlow.sln`.
- Target frameworks: `net8.0` and `net10.0`.
- The current mainline is the standalone-node architecture:
  - `FluxFlow.Nodes` `1.0.0`: shared node kit.
  - `FluxFlow.Composition` `1.0.0`: optional standalone-first composition layer
    for fluent C# and `IConfiguration` JSON. It references `FluxFlow.Nodes`,
    does not reference `FluxFlow.Engine`, uses explicit factory registration,
    validates structure, links standalone node ports directly, and owns runtime
    lifecycle/diagnostic aggregation for composed graphs.
  - `FluxFlow.Composition.Hosting` `1.0.0`: optional DI/host bridge for
    standalone compositions. It references `FluxFlow.Composition`, registers a
    hosted runtime with `IServiceCollection`, loads definitions from static
    objects or `IConfiguration`, starts/stops through `IHostedService`, exposes
    build diagnostics through `ICompositionRuntimeHost`, and provides
    keyed-resource helpers for adapter-owned resources.
  - `FluxFlow.Components.Mqtt.Composition` `1.0.0`: optional MQTT composition
    adapter registering explicit `mqtt.publish` and `mqtt.trigger` factories
    over keyed adapter-owned MQTT resources.
  - `samples/FluxFlow.MqttCompositionSample`: broker-free hosted composition
    sample showing `mqtt.trigger -> sample.mqtt.reply -> mqtt.publish` through
    both `appsettings.json` and fluent definitions.
  - `FluxFlow.Mapping` `1.0.0`: extracted mapping/expression abstractions.
  - `FluxFlow.Engine` `2.0.0`: optional legacy/advanced executable runtime.
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
- FluxFlow Pulse MQTT adapter adoption of upstream `2.4.0` passed:
  `dotnet restore tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests.csproj --nologo`,
  `dotnet build src\Mqtt\FluxFlow.Components.Mqtt.PulseMqtt\FluxFlow.Components.Mqtt.PulseMqtt.csproj --configuration Release --no-restore --nologo`,
  `dotnet test tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  (`9` passed),
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
- MQTT pilot packages were published and verified on NuGet after PR #24 merged:
  RequestReply `1.1.0`, core MQTT `4.0.0`, MQTTnet adapter `1.0.0`, and
  Pulse MQTT adapter `1.0.0`. All four release workflow runs completed
  successfully after dependency-order reruns where needed.
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
- `graphify update . --force` refreshed `graphify-out/` after recording the
  merged MQTT pilot release: 7908 nodes, 11897 edges, 749 communities.
  `graph.html` was skipped because the graph exceeds the local HTML
  visualization limit.
- `graphify update . --force` refreshed `graphify-out/` after adopting Pulse
  MQTT `2.4.0` in the FluxFlow adapter: 7921 nodes, 11917 edges,
  750 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- MQTT DI and adapter-owned feature implementation verification passed on
  2026-06-21: MQTTnet adapter Release build, Pulse adapter Release build, core
  MQTT tests (`48`), Pulse adapter tests (`12`), MQTTnet adapter tests (`23`),
  release convention tests (`33`), full solution Release tests, and package
  release preflight for `components-mqtt-mqttnet` (`1.1.0`) and
  `components-mqtt-pulsemqtt` (`1.1.0`).
- FluxFlow Pulse MQTT adapter update to upstream `2.5.0` passed on 2026-06-21:
  Pulse adapter restore, Release build, Pulse adapter tests (`12`), core MQTT
  tests (`48`), release convention tests (`33`), and package release preflight
  for `components-mqtt-pulsemqtt` (`1.1.0`). MQTTnet was checked separately and
  remains on current stable `5.1.0.1559`.
- `graphify update . --force` refreshed `graphify-out/` after updating the
  Pulse MQTT adapter to upstream `2.5.0`: 7995 nodes, 11998 edges, and
  756 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- `graphify update . --force` refreshed `graphify-out/` after the MQTT DI and
  adapter-owned feature implementation: 7989 nodes, 11992 edges, and
  757 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- Final Release verification before adapter `1.1.0` publish passed on
  2026-06-21:
  `dotnet test .\FluxFlow.sln --configuration Release --no-restore --verbosity quiet --nologo`
  passed, `eng\package-release-dry-run.ps1 -Package components-mqtt-mqttnet -Version 1.1.0`
  passed with `DRY_RUN_OK=FluxFlow.Components.Mqtt.MqttNet`, and
  `eng\package-release-dry-run.ps1 -Package components-mqtt-pulsemqtt -Version 1.1.0`
  passed with `DRY_RUN_OK=FluxFlow.Components.Mqtt.PulseMqtt`.
- The MQTTnet adapter registration now leaves hosted connect/disconnect off by
  default (`ConnectWithHost = false`) so composition layers opt in explicitly.
  Verification after the default change passed for MQTTnet adapter tests (`23`),
  release convention tests (`33`), and the MQTTnet package release dry-run
  (`DRY_RUN_OK=FluxFlow.Components.Mqtt.MqttNet`).
- `FluxFlow.Composition` v1 implementation verification passed on 2026-06-21:
  full solution Debug build, composition tests (`12`), release convention tests
  (`33`), the full no-build solution test suite, and the pure in-memory
  composition sample. The package is listed in `eng/packages.json`, has package
  release notes/changelog/readme, and is wired into `FluxFlow.sln` with its
  tests and sample.
- `graphify update . --force` refreshed `graphify-out/` after the standalone
  composition layer implementation: 8317 nodes, 12404 edges, and 799
  communities. `graph.html` was skipped because the graph exceeds the local HTML
  visualization limit.
- `FluxFlow.Composition.Hosting` v1 implementation verification passed on
  2026-06-21: full solution Debug build, composition hosting tests (`5`),
  composition tests (`12`), release convention tests (`33`), and the full
  no-build solution test suite. The package is listed in `eng/packages.json`,
  has package release notes/changelog/readme, and is wired into `FluxFlow.sln`
  with its tests.
- `graphify update . --force` refreshed `graphify-out/` after the composition
  hosting layer implementation: 8456 nodes, 12587 edges, and 814 communities.
  `graph.html` was skipped because the graph exceeds the local HTML
  visualization limit.
- `graphify update . --force` refreshed `graphify-out/` after the MQTTnet
  hosted-connect default change: 7996 nodes, 12001 edges, and 749 communities.
  `graph.html` was skipped because the graph exceeds the local HTML
  visualization limit.
