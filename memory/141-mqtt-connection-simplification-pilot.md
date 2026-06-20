# 141 - MQTT connection simplification pilot

Date: 2026-06-20

Status: merged to `main` through PR #24 and released.

## Owner direction

Use MQTT as the pilot component before planning the next broader improvement.
The MQTT component package should not create clients or interpret factory/profile
details. Hosts or future adapter packages should supply implementations of small
MQTT-facing contracts:

- `IMqttPublisher` for publish behavior.
- `IMqttTriggerSource` for trigger subscription behavior.
- `IMqttClientHealthSource` for optional client-health behavior.
- Client construction, broker configuration, reconnect policy, and concrete
  client lifetime stay outside publish/trigger nodes.

## Pilot result

- `MqttPublishNode` depends only on `IMqttPublisher`.
- `MqttTriggerNode` depends only on `IMqttTriggerSource`.
- `MqttClientUnavailableException` is the neutral signal for implementations
  that cannot publish or open a trigger subscription because no client is
  currently available.
- `IMqttSubscription` now streams `IMqttReceivedContext` values, so adapters can
  expose message-specific `AckAsync`/`NackAsync` behavior without forcing the
  node to know the concrete MQTT client library.
- `MqttTriggerNode` supports fire-and-forget mode and request/reply mode. In
  request/reply mode, downstream handlers answer on `Responses` with a
  correlated `MqttTriggerResponse`; successful responses can ack, failures and
  timeouts can nack.
- Trigger request/reply pending state now uses
  `FluxFlow.Components.RequestReply.CorrelatedRequestTracker<TContext,TResponse>`
  for duplicate detection, response matching, timeout, and shutdown cleanup.
  MQTT keeps only subscription ownership and ack/nack policy in the node.
- `MqttPublishRequest` no longer exposes a top-level string `CorrelationId`.
  Workflow correlation stays on `FlowMessage.CorrelationId`; MQTT protocol
  metadata now lives under `MqttPublishRequest.Properties` via
  `MqttPublishProperties` (`CorrelationId`, `ResponseTopic`, and user
  properties).
- `MqttPublishOptions.DefaultTopic` was removed. Publish topics are now explicit
  per `MqttPublishRequest.Topic`.
- `MqttPublishOptions` no longer carries quality-of-service or retain defaults.
  Static publish options now describe node runtime behavior only: publish
  timeout and bounded input capacity. MQTT message semantics live on
  `MqttPublishRequest`, whose quality-of-service defaults to at-most-once and
  whose retain flag defaults to false.
- Release versioning for the accepted pilot is:
  `FluxFlow.Components.RequestReply` `1.1.0` for the additive
  `CorrelatedRequestTracker`,
  `FluxFlow.Components.Mqtt` `4.0.0` for the breaking core MQTT boundary
  cleanup, and initial `FluxFlow.Components.Mqtt.MqttNet` /
  `FluxFlow.Components.Mqtt.PulseMqtt` `1.0.0` adapter packages.
- `MqttTopicValidator` remains package-owned for MQTT protocol-level topic
  rules. `MqttPublishNode` validates publish topics before calling
  `IMqttPublisher`; `MqttTriggerNode` validates static subscription filters
  before asking `IMqttTriggerSource` to subscribe. Adapter packages may add
  stricter broker or library policy, but should not be required for these
  protocol checks.
- Constructor-time option validation now covers publish bounded capacity,
  publish timeout, trigger quality-of-service defaults, trigger mode, and
  trigger acknowledgement mode. Request-time validation covers invalid publish
  quality-of-service values. The stale trigger-invalid-topic error code was
  removed because static trigger topic-filter validation fails fast before a
  trigger source is opened.
- The surviving health event name is `mqtt.client.healthChanged`
  (`ClientHealthChanged`), matching `IMqttClientHealthSource`. The old
  health-monitor node is not part of this branch.
- The duplicate `MqttDiagnosticNames` constant holder was removed. MQTT nodes
  emit `FlowEvent` values and use `MqttEventNames` as the single package-owned
  name surface.
- The core MQTT package project was moved from `src/FluxFlow.Components.Mqtt`
  to `src/Mqtt/FluxFlow.Components.Mqtt`, and the solution now has an `Mqtt`
  solution folder under `src`. Package id, assembly name, root namespace, and
  public contracts stayed unchanged. Future MQTT-related adapter packages should
  live beside the core package under `src/Mqtt/`.
- MQTT Last Will is a client-session/connect concern, not a publish/trigger
  node concern. The core package should not add will topic/payload options to
  `MqttPublishOptions` or `MqttTriggerOptions`; a future adapter package that
  owns client creation/connection can expose will message configuration and
  validate its topic with the package-owned publish-topic validator.

Removed from the live MQTT package surface:

- `MqttConnectionNode`
- `MqttHealthMonitor`
- `MqttHealthSignal`
- `IMqttClientAdapter`
- `IMqttClientFactory`
- `MqttClientFactoryContext`
- `MqttClientLease`
- `MqttReconnectPolicy`
- `MqttComponentOptions`
- `MqttConnectionOptions`
- `MqttConnectionProfile`
- `IMqttConnectionHandle`
- publish/trigger `ConnectionName` options
- the older MQTT-specific `RequestReply` helper folder

Publish/trigger nodes now depend on narrow behavior contracts. They never
create, start, stop, reconnect, or dispose the concrete client. Reconnect and
resubscribe behavior belongs behind `IMqttTriggerSource` or a future concrete
adapter package.

## Verification

- Focused RequestReply tests passed: `15` tests after adding the reusable
  correlated tracker and routing `RequestReplyCoordinator` through it.
- Focused MQTT tests passed: `48` tests after removing connection-helper tests,
  adding trigger request/reply ack/nack coverage, moving publish protocol
  correlation into `MqttPublishProperties`, removing publish default-topic
  fallback, wiring trigger request/reply to the shared tracker, and adding
  publish/trigger static-option validation coverage. The added topic-filter
  cases verify trigger option validation uses the package-owned protocol
  validator.
- Full solution Release test passed with:
  `dotnet test .\FluxFlow.sln --configuration Release --no-restore --verbosity quiet --nologo`.
- Full solution Release test passed again after the MQTT review cleanup with:
  `dotnet test .\FluxFlow.sln --configuration Release --no-restore --verbosity quiet --nologo`.
- Focused MQTT tests passed after the project-layout move:
  `dotnet test tests\FluxFlow.Components.Mqtt.Tests\FluxFlow.Components.Mqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  (`48` passed). A restore was run once first so assets used the new project
  path.
- Release convention tests passed after the project-layout move:
  `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  (`33` passed).
- Focused MQTT tests passed after removing quality-of-service and retain from
  `MqttPublishOptions` and making them request-owned publish semantics:
  `dotnet test tests\FluxFlow.Components.Mqtt.Tests\FluxFlow.Components.Mqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  (`48` passed),
  `dotnet test tests\FluxFlow.Components.Mqtt.MqttNet.Tests\FluxFlow.Components.Mqtt.MqttNet.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  (`19` passed), and
  `dotnet test tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  (`8` passed). Release convention tests also passed at `33` tests.
- Release preflight passed for `components-requestreply` `1.1.0`,
  `components-mqtt` `4.0.0`, `components-mqtt-mqttnet` `1.0.0`, and
  `components-mqtt-pulsemqtt` `1.0.0`; each resolved a changelog section and
  release tag.
- Full solution Release verification passed with:
  `dotnet test .\FluxFlow.sln --configuration Release --no-restore --verbosity quiet --nologo`.
- Fast package dry-runs passed for all four release packages. Each dry-run
  packed the `.nupkg`/`.snupkg`, inspected the archive, ran the local consumer
  smoke project, and verified restore/build from the local artifact source plus
  NuGet.
- PR #24 (`Finalize MQTT package release`) merged into `main` with squash merge
  commit `118a06de613a9ebdfd47e9e06b7c6761161a4d37`.
- Stable GitHub releases and NuGet packages were published and verified:
  `FluxFlow.Components.RequestReply` `1.1.0`
  (`components-requestreply-v1.1.0`, run `27877804072`),
  `FluxFlow.Components.Mqtt` `4.0.0`
  (`components-mqtt-v4.0.0`, run `27877844606`),
  `FluxFlow.Components.Mqtt.MqttNet` `1.0.0`
  (`components-mqtt-mqttnet-v1.0.0`, run `27877876917`), and
  `FluxFlow.Components.Mqtt.PulseMqtt` `1.0.0`
  (`components-mqtt-pulsemqtt-v1.0.0`, run `27877966707`).
  The core MQTT and adapter workflows initially hit dependency-index ordering
  before their just-published dependencies were visible on NuGet; rerunning in
  dependency order passed, and explicit public-feed verification returned
  `FEED_OK` for all four package IDs.
- Full solution Release tests passed after the project-layout move:
  `dotnet test .\FluxFlow.sln --configuration Release --no-restore --verbosity quiet --nologo`.
- `graphify update . --force` refreshed local graph output after adding the
  MQTTnet adapter package and updating memory: 7783 nodes, 11712 edges,
  740 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- `graphify update . --force` refreshed local graph output after recording the
  merged MQTT pilot release: 7908 nodes, 11897 edges, 749 communities.
  `graph.html` was skipped because the graph exceeds the local HTML
  visualization limit.
- Live `src`/`tests` references to the removed factory/profile/lease types were
  eliminated. Remaining mentions are historical changelog and older memory
  records.

## Next improvement criteria

Before applying this pattern elsewhere, check the pilot for:

- host ergonomics: direct constructor/injection feels simpler than a factory
  context;
- lifecycle clarity: publish/trigger nodes do not need start/stop, state,
  epoch, borrow-only adapter access, or a package-provided connection helper;
- event clarity: state and health events are sufficient for observers;
- compatibility cost: alias methods and removed factory/profile types are an
  acceptable break for the current branch line;
- pattern reuse: only extract a shared helper if a second component needs the
  same correlation/timeout shape. The current reusable extraction is intentionally
  narrower than a full request/reply bridge.

Recommended next planning step: review the MQTT pilot diff first, then choose
one adjacent connection-style surface only if it has the same SRP problem. Do
not generalize from the pilot until the direct-adapter shape is accepted.

## MQTT adapter boundary direction

The core MQTT component package should stay client-library-neutral. Nodes such
as publisher, subscriber, and trigger components should depend only on the small
MQTT-facing contracts:

- `IMqttPublisher` for publish behavior;
- `IMqttTriggerSource` for trigger subscription behavior;
- `IMqttClientHealthSource` for optional health behavior;
- `IMqttReceivedContext` / `MqttTriggerResponse` for trigger request/reply and
  manual acknowledgement behavior.

That keeps the core MQTT nodes focused on dataflow behavior and correlation,
not connection state or concrete broker-client implementation details. Different
client libraries can be supported later through separate adapter packages that
implement these contracts and own their own configuration, connection,
reconnect, logging, and library-specific behavior.

Shared request/reply code should stay transport-neutral. MQTT should depend on
the shared correlation tracker only for pending response mechanics; it should not
push MQTT acknowledgement semantics into `FluxFlow.Components.RequestReply`.

Last Will configuration should also stay outside the core node contracts. It is
registered with the broker during MQTT `CONNECT` and published by the broker only
on unexpected disconnect. A graceful workflow "offline" publish is a separate
normal publish behavior and should not be modeled as Last Will.

After the interface cleanup was accepted, provider-named adapter packages were
accepted for concrete client-library integrations. The naming rule is: keep the
core MQTT component package neutral, and use a concrete library/provider name
only in the separate adapter package that actually references that library.
The first accepted implementation is `FluxFlow.Components.Mqtt.MqttNet`; see
`142-mqttnet-adapter-package.md`.

## Adjacent scan after pilot

- HTTP is already in the direct-dependency shape: `HttpClientNode` accepts an
  injected `HttpClient`.
- Storage operation nodes are already direct over an injected `IStorageStore`.
- The remaining storage cleanup candidate is the adapter/registration convenience
  layer: `IStorageStoreFactory`, `StorageStoreLease`, `StorageComponentOptions`,
  and the file-system/sql-file factory helpers. That is not the same as the old
  MQTT node-level factory problem, so treat it as a separate scoped API cleanup,
  not an automatic copy of the MQTT pilot.
