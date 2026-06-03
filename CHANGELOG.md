# Changelog

## FluxFlow.Components.Resources 0.1.0-alpha.1

Resources package.

- Adds `FluxFlow.Components.Resources`.
- Adds neutral named resource reference and descriptor contracts.
- Adds lookup result contracts with structured resource diagnostics.
- Adds missing, duplicate, unused, kind mismatch, and invalid resource
  diagnostics.
- Adds an in-memory descriptor catalog for host composition.

## FluxFlow.Components.Designer 0.1.0-alpha.1

Designer metadata package.

- Adds `FluxFlow.Components.Designer`.
- Adds neutral component display metadata contracts.
- Adds option metadata for text, number, boolean, enum, multiline text, JSON,
  expression, duration, and secret values.
- Adds port metadata for ordering and grouping.
- Adds a catalog and provider helper for host composition.

## FluxFlow.Components.Expectations 0.1.0-alpha.1

Event expectations package.

- Adds `FluxFlow.Components.Expectations`.
- Adds `event.expect` for matching expected runtime events.
- Adds `event.guard` for guarding against matching runtime events.
- Adds `EventExpectationResult` and expectation result kind contracts.
- Reuses the neutral event filter and event summary contracts from
  `FluxFlow.Components.Projections`.
- Supports timeout results and deterministic evaluation timestamps through a
  host-provided expectation clock.

## FluxFlow.Components.Projections 0.1.0-alpha.1

Event projection package.

- Adds `FluxFlow.Components.Projections`.
- Adds `event.projection` for in-memory runtime event projections.
- Adds `EventFilter`, `EventSummary`, and `EventProjectionSnapshot`.
- Supports count, latest matching event, payload preview, and rolling rate.
- Supports deterministic snapshot timestamps through a host-provided projection
  clock.

## FluxFlow.Components.Mqtt 0.5.0-alpha.1

Adapter-owned reconnect policy hints.

- Adds `MqttReconnectPolicy`.
- Adds optional `reconnect` settings to `mqtt.publish` and `mqtt.subscribe`.
- Passes reconnect policy hints through `MqttClientFactoryContext`.
- Keeps retry loops and connection recovery owned by host adapters.

## FluxFlow.Components.Sessions 0.3.0-alpha.1

Session metadata query node.

- Adds `session.query` for neutral session metadata queries.
- Adds `SessionQueryRequest` and `SessionQueryResult` contracts.
- Adds aggregate query results plus optional per-session outputs.
- Keeps concrete persistence and retention policy outside the package.

## FluxFlow.Components.Routing 0.10.0-alpha.1

Explicit routing result timestamps.

- Makes `FlowSwitchResult.EvaluatedAt`, `FlowRoute.RoutedAt`,
  `FlowCorrelationMatch.MatchedAt`, `FlowJoinResult.JoinedAt`, and
  `FlowMergeItem.ReceivedAt` required values instead of current-time defaults.
- Routing nodes already set these values from the configured routing clock, so
  package-owned timestamps stay deterministic.

## FluxFlow.Components.Mqtt 0.4.0-alpha.1

Deterministic MQTT package timestamps.

- Adds `IMqttClock` and `SystemMqttClock`.
- Adds `UseClock(...)` to `MqttComponentOptions`.
- `mqtt.publish` now uses the configured clock for publish result timestamps.
- MQTT publish, subscribe, and connection health events now use the configured
  clock for package-owned event timestamps.
- `MqttClientFactoryContext` now carries the configured clock to host adapter
  factories.

## FluxFlow.Components.Storage 0.3.0-alpha.1

Deterministic logical storage timestamps.

- Adds `IStorageClock` and `SystemStorageClock`.
- Adds `UseClock(...)` to `StorageComponentOptions`.
- `storage.put`, `storage.get`, and `storage.query` now use the configured
  clock for emitted result timestamps.
- `StorageStoreContext` now carries the configured clock to backend store
  factories.

## FluxFlow.Components.Storage.FileSystem 0.2.0-alpha.1

Deterministic file-system storage adapter timestamps.

- `FileSystemStorageStore` now uses the configured storage clock for
  `StorageRecord.StoredAt`, delete result timestamps, and expiration checks.
- `FileSystemStorageStoreOptions.Clock` can override the context clock for
  direct store construction.

## FluxFlow.Components.Storage.SqlFile 0.2.0-alpha.1

Deterministic SQL-file storage adapter timestamps.

- `SqlFileStorageStore` now uses the configured storage clock for
  `StorageRecord.StoredAt`, delete result timestamps, query expiration
  filtering, and in-memory expiration checks.
- `SqlFileStorageStoreOptions.Clock` can override the context clock for direct
  store construction.

## FluxFlow.Components.Validation 0.2.0-alpha.1

Deterministic validation result timestamps.

- Adds `IValidationClock` and `SystemValidationClock`.
- Adds `UseClock(...)` to `ValidationComponentOptions`.
- `json.schema-validator` now uses the configured clock for
  `JsonSchemaValidationResult<TInput>.Timestamp`.

## FluxFlow.Components.FileSystem 0.5.0-alpha.1

Deterministic file system timestamps.

- Adds `IFileSystemClock` and `SystemFileSystemClock`.
- Adds `UseClock(...)` to `FileSystemComponentOptions`.
- `file.write`, `file.read`, `file.watch`, and `directory.enumerate` now use
  the configured clock for emitted timestamps.
- Keeps the existing static node `Create(context)` methods as default-clock
  wrappers.

## FluxFlow.Components.Http 0.2.0-alpha.1

Deterministic HTTP request timing.

- Adds `IHttpClock` and `SystemHttpClock`.
- Adds `UseClock(...)` to `HttpComponentOptions`.
- `http.request` now uses the configured clock for response and error
  timestamps plus elapsed milliseconds.
- `HttpRequestSenderContext` now exposes the configured clock to sender
  factories.

## FluxFlow.Components.State 0.3.0-alpha.1

Deterministic state result timestamps.

- Adds `IStateClock` and `SystemStateClock`.
- Adds `UseClock(...)` to `StateComponentOptions`.
- `state.reducer` now uses the configured clock for `StateReducerResult`
  timestamps on reduce, reset, and clear operations.

## FluxFlow.Components.Observability 0.3.0-alpha.1

Deterministic observer timestamps.

- Adds `IObservabilityClock` and `SystemObservabilityClock`.
- Adds `UseClock(...)` to `ObservabilityComponentOptions`.
- `flow.logger`, `flow.counter`, and `flow.metrics` now use the configured
  clock for emitted timestamps.
- `flow.metrics` rate calculations now use the configured clock.

## FluxFlow.Components.Routing 0.9.0-alpha.1

Deterministic routing timing.

- Adds `IRoutingClock` and `SystemRoutingClock`.
- Adds `UseClock(...)` to `RoutingComponentOptions`.
- `flow.switch`, `flow.merge`, `flow.window`, `flow.join`, and
  `flow.correlation` now use the configured clock for emitted timestamps.
- `flow.window` and `flow.join` now use the configured clock for timeout
  delays.

## FluxFlow.Components.Metrics 0.2.0-alpha.1

Deterministic metric fallback timestamps.

- Adds `IMetricsClock` and `SystemMetricsClock`.
- Adds `UseClock(...)` to `MetricsComponentOptions`.
- `metrics.aggregate` now uses the configured clock only when a
  `MetricSampleInput` omits `Timestamp`.

## FluxFlow.Components.Timers 0.5.0-alpha.1

Deterministic timer timing.

- Adds `ITimerClock` and `SystemTimerClock`.
- Adds `UseClock(...)` to `TimerComponentOptions`.
- `timer.interval`, `timer.schedule`, `timer.delay`, `timer.throttle`, and
  `timer.debounce` now use the configured clock for timestamps and delays.
- `timer.interval` and `timer.schedule` now emit their started diagnostics
  before background work begins.

## FluxFlow.Components.FileSystem 0.4.2-alpha.1

Directory enumerate diagnostic reliability.

- `directory.enumerate` now emits `directory.enumerate.started` before the
  background enumeration task begins.
- Fixes a race where very fast enumerations could complete diagnostics before
  the startup diagnostic was accepted.

## FluxFlow.Components.Sessions 0.2.0-alpha.1

Deterministic session timing.

- Adds `ISessionClock` and `SystemSessionClock`.
- Adds `UseClock(...)` to `SessionsComponentOptions`.
- `session.recorder` uses the configured clock for session start/end
  timestamps and default message timestamps.
- `session.replay` uses the configured clock for replay delays.

## FluxFlow.Components.Sources 0.2.0-alpha.1

Deterministic source timing.

- Adds `ISourceClock` and `SystemSourceClock`.
- Adds `UseClock(...)` to `SourcesComponentOptions`.
- `source.sequence` now uses the configured clock for item timestamps.
- `source.generated` and `source.sequence` use the configured clock for
  initial and interval delays.

## FluxFlow.Components.Storage.SqlFile 0.1.0-alpha.1

First single-file SQL storage adapter package.

- Adds `SqlFileStorageStore`, `SqlFileStorageStoreFactory`, and
  `SqlFileStorageStoreOptions`.
- Adds `UseSqlFileStorage(...)` registration helpers for existing storage
  nodes.
- Supports put, get, query, delete, write modes, expected versions,
  expiration, attributes, and store/collection defaults.

## FluxFlow.Components.Mqtt 0.3.0-alpha.1

MQTT adapter health forwarding.

- Adds optional `IMqttClientHealthSource`, `MqttClientHealthEvent`, and
  `MqttClientHealthState` contracts.
- Forwards adapter health from `mqtt.publish` and `mqtt.subscribe` as
  diagnostics and events named `mqtt.connection.healthChanged`.
- Keeps reconnect policy host/adapter-owned.

## FluxFlow.Components.Routing 0.8.0-alpha.1

Expression support hardening.

- Uses `FluxFlow.Components.Expressions` internally for expression engine and
  context factory registration.
- Preserves the existing public Routing registration API.
- No routing node port or runtime behavior changes.

## FluxFlow.Components.Observability 0.2.0-alpha.1

Expression support hardening.

- Uses `FluxFlow.Components.Expressions` internally for expression engine and
  context factory registration.
- Preserves the existing public Observability registration API.
- No observer node port or runtime behavior changes.

## FluxFlow.Components.State 0.2.0-alpha.1

Expression support hardening.

- Uses `FluxFlow.Components.Expressions` internally for expression engine
  registration.
- Preserves the existing public State registration API.
- No state reducer port or runtime behavior changes.

## FluxFlow.Components.Assertions 0.2.0-alpha.1

Expression support hardening.

- Uses `FluxFlow.Components.Expressions` internally for expression engine and
  context factory registration.
- Preserves the existing public Assertions registration API.
- No assertion port or runtime behavior changes.

## FluxFlow.Components.Control 0.3.0-alpha.1

Expression support hardening.

- Uses `FluxFlow.Components.Expressions` internally for expression engine and
  context factory registration.
- Preserves the existing public Control registration API.
- No control port or runtime behavior changes.

## FluxFlow.Components.Mapping 0.2.0-alpha.1

Expression support hardening.

- Uses `FluxFlow.Components.Expressions` internally for expression engine and
  context factory registration.
- Preserves the existing public Mapping registration API.
- No mapper port or runtime behavior changes.

## FluxFlow.Components.Expressions 0.1.0-alpha.1

First supporting package for component expression registration.

- Adds `FlowExpressionEngineRegistry` for named/default expression engines and
  host-provided expression engine resolvers.
- Adds `FlowContextFactoryRegistry<TFactory>` for exact, assignable, and
  default context factory resolution by input type.
- Does not include a concrete expression language.

## FluxFlow.Components.Routing 0.7.0-alpha.1

Routing correlation hardening.

- Adds split `Request` and `Response` input ports for `flow.correlation` when
  `sideExpression` is omitted.
- Keeps the existing single-stream `Input` mode when `sideExpression` is
  configured.
- Keeps `Matched`, `Timeouts`, and `Errors` output contracts unchanged.

## FluxFlow.Engine 1.0.0

Stable engine release.

- Confirms `FluxFlow.Engine` as the stable protocol-neutral runtime package for
  executable workflow definitions, typed ports, reliable fanout, conditional
  links, runtime state, diagnostics, events, host lifecycle, and node authoring.
- Keeps concrete protocols, storage backends, dashboards, scenarios, and
  expression-language implementations outside the engine package.
- Keeps component packages on their own independent prerelease or stable tracks.
- Adds public API overview, compatibility guidance, and migration guidance from
  the `0.5.0-alpha.1` line to the beta/stable boundary.
- Validated the beta package with the first consumer migration before promoting
  the engine to stable.
- No public API changes were added after `0.6.0-beta.1`; `1.0.0` is the stable
  promotion of the beta engine boundary.

## FluxFlow.Components.Assertions 0.1.1-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Components.Control 0.2.1-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Components.FileSystem 0.4.1-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Components.Http 0.1.1-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Components.Mapping 0.1.1-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Components.Metrics 0.1.1-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Components.Mqtt 0.2.2-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Components.Observability 0.1.1-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Components.Payloads 0.1.1-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Components.Routing 0.6.1-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Components.Serialization 0.1.1-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Components.Sessions 0.1.1-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Components.Sources 0.1.1-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Components.State 0.1.1-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Components.Storage 0.2.1-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Components.Storage.FileSystem 0.1.1-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Components.Timers 0.4.2-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Components.Validation 0.1.1-alpha.1

Engine compatibility rebuild.

- Rebuilt the package against `FluxFlow.Engine` `1.0.0`.
- Aligns package binaries with the stable `FlowNodeId` namespace in
  `FluxFlow.Engine.Components`.
- No component behavior changes.

## FluxFlow.Engine 0.6.0-beta.1

Engine beta readiness release.

- Moved `FlowNodeId` into `FluxFlow.Engine.Components` so the node-authoring
  surface has one stable namespace.
- Removed concrete expression-language adapters from the engine package.
- Removed expression parser package dependencies from `FluxFlow.Engine`.
- Kept expression abstractions, mapper/predicate helpers, and conditional-link
  runtime support in the engine.
- Changed link `when` conditions to require a host-provided
  `IFlowExpressionEngine` through `ApplicationRuntimeBuilder` or
  `FlowApplicationHost.Create(...)`.
- Added `ApplicationRuntimeBuildErrorCode.MissingExpressionEngine` for
  definitions that use `when` without a supplied expression engine.
- Added host factory overloads that accept the link-condition expression
  engine directly.
- Updated public docs and the sample app for the host-provided expression
  boundary.
- Stabilized the routing join capacity test so release verification no longer
  relies on cross-input scheduling order.

## FluxFlow.Components.Storage.FileSystem 0.1.0-alpha.1

Initial file-system-backed storage adapter package.

- Added `FluxFlow.Components.Storage.FileSystem` as a separate source project,
  test project, and package artifact.
- Added `FileSystemStorageStore`, `FileSystemStorageStoreFactory`,
  `FileSystemStorageStoreOptions`, and `UseFileSystemStorage(...)`
  registration helpers.
- Persisted one JSON file per storage record under hashed store, collection,
  and key paths.
- Added support for create, replace, upsert, expected version checks,
  expiration-aware reads, found/missing delete results, and query.
- Added query filtering by collection, key prefix, exact-match attributes,
  stored time bounds, expiration policy, and limit.
- Added value size validation and best-effort temporary-file replacement on
  writes.
- Added focused coverage for persistence, write modes, version checks,
  expiration, deletes, query behavior, path safety, size limits, factory
  defaults, option validation, and registration through storage nodes.

## FluxFlow.Components.Storage 0.2.0-alpha.1

Logical storage query primitive.

- Added `storage.query` with `Input`, `Result`, `Records`, and `Errors` ports.
- Added `StorageQueryRequest` and `StorageQueryResult` contracts.
- Added `IStorageStore.QueryAsync(...)` for host-provided stores.
- Added filters for collection, key prefix, exact-match attributes, stored time
  bounds, expiration policy, and limit.
- Added options for default collection, default limit, result record payloads,
  per-record output emission, and bounded capacity.
- Added per-message query failures as `FlowError` so later messages continue.
- Added diagnostics for query completion and query failures.
- Updated the storage composition sample to include `storage.query`.
- Added focused coverage for query results, record streaming, suppression
  options, failures, registration, and sample store behavior.

## FluxFlow.Components.Routing 0.6.0-alpha.1

Merge, fork, and route-envelope primitives.

- Added `flow.fork` with one typed `Input`, configured named outputs, and
  `Errors`.
- Added reliable per-message fan-out from `flow.fork` to every configured
  output in configured order.
- Added `flow.merge` with configured named inputs, `Output`, and `Errors`.
- Added `FlowMergeItem<TInput>` with source input port, sequence number,
  received timestamp, and original value.
- Added optional `Routed` output on `flow.switch`.
- Added `FlowRoute<TInput>` with route key, selected route, match status,
  default route, matched output port, expression metadata, input type, and
  original value.
- Added switch `emitRouteEnvelope` option so hosts can compose route metadata
  without relying only on dynamic route ports.
- Added validation for fork output names and merge input names, including empty
  values, duplicates, invalid port names, and built-in port collisions.
- Added diagnostics for fork forwarding and merge output emission.
- Added focused coverage for fork fan-out, merge source tagging, switch route
  envelopes, completion, diagnostics, invalid config, unknown input types, and
  module registration.

## FluxFlow.Components.Routing 0.5.0-alpha.1

Two-stream join primitive.

- Added `flow.join` with `Left`, `Right`, `Output`, `Timeouts`, and `Errors`
  ports.
- Added `FlowJoinResult<TLeft, TRight>`,
  `FlowJoinTimeout<TLeft, TRight>`, and `FlowJoinSide` contracts.
- Added per-side key expressions through the host-provided expression engine
  and context factories.
- Added FIFO pairing for repeated keys across left and right streams.
- Added timer-driven timeout output plus completion flush for unmatched values.
- Added bounded pending-item tracking through `maxPending`.
- Added recoverable errors for key failures, invalid keys, and pending
  capacity failures.
- Added diagnostics for joined values, timeouts, and recoverable failures.
- Added focused coverage for in-order and out-of-order joins, duplicate-key
  order, timer timeouts, completion timeouts, expression failures, capacity,
  diagnostics, invalid config, unknown input types, and module registration.

## FluxFlow.Components.Routing 0.4.0-alpha.1

Window routing primitive.

- Added `flow.window` with `Input`, `Output`, and `Errors` ports.
- Added `FlowWindow<TInput>` and `FlowWindowEmitReason` contracts.
- Added count-based window emission through `maxItems`.
- Added time-based window emission through `timeMilliseconds`.
- Added partial window flush on completion with an option to suppress partials.
- Added diagnostics for emitted windows.
- Added focused coverage for count windows, time windows without a next input,
  count-before-time behavior, completion partials, empty completion, diagnostics,
  invalid config, and module registration.

## FluxFlow.Components.Routing 0.3.0-alpha.1

Switch route-output hardening.

- Added optional `routeOutputs` configuration for `flow.switch`.
- Added dynamic route-specific output ports that emit the original input for
  matched route keys.
- Added support for mapping multiple route keys to one output port.
- Added validation for empty route output keys, missing route declarations,
  invalid port names, and built-in port collisions.
- Added focused coverage for route-specific outputs, shared output ports, and
  invalid route-output configuration.

## FluxFlow.Components.Routing 0.2.0-alpha.2

Routing correlation published build.

- Keeps the `flow.correlation` component and contracts added for the 0.2
  routing line.
- Hardens the timer interval completion test so release verification drains
  buffered ticks before waiting for completion.
- Supersedes `0.2.0-alpha.1`, which did not publish a package.

## FluxFlow.Components.Routing 0.2.0-alpha.1

Routing correlation addition.

- Added `flow.correlation` with `Input`, `Matched`, `Timeouts`, and `Errors`
  ports.
- Added `FlowCorrelationMatch<TInput>` and `FlowCorrelationTimeout<TInput>`
  contracts.
- Added key and side expression evaluation through the host-provided expression
  engine and context factories.
- Added bounded pending-key tracking with configurable timeout, side names,
  case sensitivity, max pending keys, and bounded capacity.
- Added per-message errors for key failures, invalid keys, side failures,
  invalid sides, duplicate sides, and pending capacity.
- Added diagnostics for matched pairs, timeout output, and recoverable failures.
- Added focused coverage for in-order and out-of-order matching, completion
  timeouts, observed timeouts, expression failures, invalid sides, capacity,
  diagnostics, invalid config, and module registration.

## FluxFlow.Components.Routing 0.1.0-alpha.1

Initial reusable routing component package.

- Added `FluxFlow.Components.Routing` as a separate source project, test
  project, and package artifact.
- Added `flow.switch` with `Input`, `Result`, `Matched`, `Default`, and
  `Errors` ports.
- Added `FlowSwitchResult<TInput>` route result contract.
- Added host-provided expression engine resolution, type aliases, and context
  factories.
- Added configured route matching with optional case-insensitive comparison.
- Added route-result output plus matched/default original input streams.
- Added optional matched/default input emission controls.
- Added per-message expression failures as `FlowError` so later messages
  continue.
- Added diagnostics with input type, engine, route key, match status,
  expression id, and expression name metadata where available.
- Added focused coverage for matched/default routing, empty route sets,
  case-insensitive matching, suppression flags, expression failures,
  diagnostics, registration, missing expression, unknown input types, and
  invalid route config.

## FluxFlow.Components.Sources 0.1.0-alpha.1

Initial reusable deterministic source component package.

- Added `FluxFlow.Components.Sources` as a separate source project, test
  project, and package artifact.
- Added `source.generated` with typed `Output` and `Errors` ports.
- Added `source.sequence` with `Output` and `Errors` ports.
- Added host-registered output type aliases for generated source items.
- Added configured JSON item conversion for generated source output.
- Added generated list loop/max item controls.
- Added initial delay, interval, and bounded output options.
- Added `SourceSequenceItem` for deterministic sequence output.
- Added diagnostics for source start, item emission, completion, and failure.
- Added focused coverage for typed output, loop behavior, empty completion,
  diagnostics, cancellation, registration, invalid options, unsupported types,
  and conversion failures.

## FluxFlow.Components.Assertions 0.1.0-alpha.1

Initial reusable assertion component package.

- Added `FluxFlow.Components.Assertions` as a separate source project, test
  project, and package artifact.
- Added `flow.assert` with `Input`, `Result`, `Passed`, `Failed`, and `Errors`
  ports.
- Added `FlowAssertionResult`, `FlowAssertionStatus`, and `AssertionFailure`
  contracts.
- Added host-provided expression engine resolution, type aliases, and context
  factories.
- Added optional pass/fail input routing controls.
- Added per-message expression failures as `FlowError` so later messages
  continue.
- Added diagnostics with input type, engine, expression id, expression name, and
  pass metadata where available.

## FluxFlow.Components.Control 0.2.0-alpha.1

Control package boundary cleanup.

- Kept `flow.filter` and `flow.when` in the control package.
- Moved `flow.assert` ownership to the assertions package.
- Removed assertion-only result contracts, ports, diagnostics, and error codes
  from the control package.

## FluxFlow.Components.Storage 0.1.0-alpha.1

Initial reusable logical storage component package.

- Added `FluxFlow.Components.Storage` as a separate source project, test
  project, and package artifact.
- Added `storage.put`, `storage.get`, and `storage.delete` nodes.
- Added storage request/result/record contracts and write modes.
- Added host-injected store factories and explicit store ownership leases.
- Added startup failure handling when stores cannot be opened.
- Added per-message store failures as `FlowError` with later-message
  continuation.
- Added found and not-found routing for `storage.get`.
- Added missing delete result control for `storage.delete`.
- Added diagnostics for store open, put, get, delete, and failure paths.
- Added focused coverage for put modes, get routing, expiration, delete
  behavior, startup failure, invalid requests, diagnostics, registration, and
  lease disposal.

## Documentation

- Added component composition guidance for host boundaries, package boundaries,
  common node shapes, extraction timing, and independent package movement.
- Linked component package READMEs back to the shared composition guidance.

## FluxFlow.Components.State 0.1.0-alpha.1

Initial reusable state reducer component package.

- Added `FluxFlow.Components.State` as a separate source project, test
  project, and package artifact.
- Added `state.reducer` with `Input`, `Output`, and `Errors` ports.
- Added `StateReducerInput`, `StateReducerResult`, and
  `StateReducerOperation` contracts.
- Added per-key in-memory state with reducer expression updates.
- Added optional key expression support.
- Added bounded key cardinality through `maxKeys`.
- Added reset and clear operations on the reducer input stream.
- Added reducer/key failure errors with later-message continuation.
- Added diagnostics for update, reset, clear, failures, and key limits.
- Added focused coverage for state updates, initial state, key expressions,
  reset/clear, reducer failures, key limits, diagnostics, registration, and
  option validation.

## Samples

- Added `samples/FluxFlow.StateCompositionSample`, a runnable composition sample
  that maps timer ticks into `state.reducer` and observes reducer outputs with
  `flow.counter`.
- Added `samples/FluxFlow.SessionsCompositionSample`, a runnable composition
  sample that records neutral session messages into a host-owned store and then
  replays them through `session.replay`.

## FluxFlow.Components.Sessions 0.1.0-alpha.1

Initial reusable session recording and replay component package.

- Added `FluxFlow.Components.Sessions` as a separate source project, test
  project, and package artifact.
- Added `session.recorder` with `Input`, `Output`, and `Errors` ports.
- Added `session.replay` with `Output` and `Errors` ports.
- Added `SessionRecordInput`, `SessionRecord`, `SessionMetadata`, store
  request contracts, `ISessionStore`, and `ISessionStoreFactory`.
- Added host-injected storage through `SessionsComponentOptions`.
- Added ordered recording with per-message append failures reported as
  structured errors.
- Added replay modes for instant, fixed interval, timestamp delta, and
  multiplier timing.
- Added replay range options through `startSequence` and `maxMessages`.
- Added diagnostics for recorder and replay lifecycle/messages.
- Added focused coverage for recording order, append failure continuation,
  replay order, replay timing, cancellation, missing sessions, diagnostics,
  store injection, registration, and option validation.

## FluxFlow.Components.Metrics 0.1.0-alpha.1

Initial reusable metrics aggregation component package.

- Added `FluxFlow.Components.Metrics` as a separate source project, test
  project, and package artifact.
- Added `metrics.aggregate` with `Input`, `Output`, and `Errors` ports.
- Added `MetricSampleInput`, `MetricSnapshotOutput`, and
  `MetricGroupSnapshot` contracts.
- Added sample count, numeric total, average, min, max, current rate, average
  rate, total size, latest sample, and grouped snapshot aggregation.
- Added deterministic rate calculations from sample timestamps.
- Added bounded group cardinality through `maxGroups`.
- Added non-blocking snapshot output with structured dropped-snapshot errors.
- Added options for rate window, bounded capacity, group limit, per-sample
  emission, latest tracking, min/max tracking, size tracking, tag grouping, and
  missing-value handling.
- Added focused coverage for counts, sizes, rates, grouping, latest/final
  snapshots, missing values, group limits, invalid samples, unlinked output,
  diagnostics, registration, and option validation.

## FluxFlow.Components.Serialization 0.1.0-alpha.1

Initial reusable serialization component package.

- Added `FluxFlow.Components.Serialization` as a separate source project, test
  project, and package artifact.
- Added `json.parse` and `json.stringify` nodes.
- Added `text.encode` and `text.decode` nodes.
- Added `base64.encode` and `base64.decode` nodes.
- Added request and result contracts for JSON, text, and base64 conversions.
- Added bounded capacity, default encoding, input byte limit, output byte
  limit, JSON indentation, trailing comma, and comment handling options.
- Added structured errors for parse, stringify, encode, decode, missing input,
  unsupported encoding, oversized input, and oversized output failures.
- Added diagnostics for successful and failed conversions.
- Added focused coverage for each node, per-message error continuation,
  diagnostics, registration, and option validation.

## FluxFlow.Components.Http 0.1.0-alpha.1

Initial reusable HTTP request component package.

- Added `FluxFlow.Components.Http` as a separate source project, test project,
  and package artifact.
- Added `http.request` with `Input`, `Output`, and `Errors` ports.
- Added `HttpRequestInput`, `HttpResponseOutput`, `HttpErrorOutput`, and
  `HttpErrorKind` contracts.
- Added host-replaceable `IHttpRequestSenderFactory` and `IHttpRequestSender`
  contracts.
- Added default per-node HTTP sender without static/shared client state.
- Added base URL, default headers, timeout, redirect, response body limit,
  non-success routing, and bounded capacity options.
- Added structured errors for invalid request, invalid URL, timeout,
  cancellation, network failure, response body size, send failure, and
  non-success status.
- Added focused coverage for sender replacement, request resolution, response
  routing, error routing, timeouts, response body limits, diagnostics, and
  registration.

## FluxFlow.Components.Payloads 0.1.0-alpha.1

Initial reusable payload inspection component package.

- Added `FluxFlow.Components.Payloads` as a separate source project, test
  project, and package artifact.
- Added `payload.inspect` with `Input`, `Output`, and `Errors` ports.
- Added `PayloadInspectionRequest`, `PayloadInspectionResult`, and
  `PayloadKind` contracts.
- Added classification for empty, JSON object, JSON array, JSON scalar, XML,
  base64, text, and binary payloads.
- Added preview metadata for byte count, detected encoding, text preview,
  formatted preview, parse errors, truncation flags, and base64 decoded size.
- Added byte payload decoding from explicit encoding hints or content type
  charset values.
- Added configurable preview limits, formatting controls, base64 detection,
  and bounded capacity.
- Added focused coverage for classification, preview truncation, error
  emission, continuation after per-message failures, diagnostics, and
  completion behavior.

## FluxFlow.Components.Timers 0.4.1-alpha.1

Finalizes the first timer component set.

- Added a shared internal typed node factory path for `timer.delay`,
  `timer.throttle`, and `timer.debounce`.
- Fixed throttle emission timestamp ordering so immediate downstream feedback
  cannot bypass the configured interval.
- Kept existing timer node contracts, ports, options, and behavior unchanged.
- Preserved focused coverage for interval, schedule, delay, throttle, and
  debounce nodes.

## FluxFlow.Components.Timers 0.4.0-alpha.1

Adds typed debounce for quiet-period workflow paths.

- Added `timer.debounce` with typed `Input` and `Output` ports.
- Added host-registered input type aliases for typed debounce pass-through.
- Added quiet-period options and bounded capacity.
- Added completion flushing for the latest pending input.
- Added debounce diagnostics with input type, quiet period, sequence, and node
  name.
- Added validation for quiet period, input type, duplicate quiet-period
  options, and bounded capacity.

## FluxFlow.Components.Timers 0.3.0-alpha.1

Adds typed throttling for rate-limited workflow paths.

- Added `timer.throttle` with typed `Input` and `Output` ports.
- Added host-registered input type aliases for typed throttle pass-through.
- Added interval options, immediate-first-item control, and bounded capacity.
- Added throttle diagnostics with input type, interval, sequence, and node name.
- Added validation for interval, input type, duplicate interval options, and
  bounded capacity.

## FluxFlow.Components.Timers 0.2.0-alpha.1

Adds delay and cron schedule timing nodes.

- Added `timer.delay` with typed `Input` and `Output` ports.
- Added host-registered input type aliases for typed delay pass-through.
- Added `timer.schedule` with an `Output` port.
- Added `ScheduleTick` with timestamp, due time, sequence, cron, time zone, and
  drift metadata.
- Added five-field and six-field cron parsing with lists, ranges, steps,
  question-mark day wildcards, and month/day names.
- Added schedule options for time zone, max ticks, and bounded capacity.
- Added delay and schedule diagnostics, plus workflow events for schedule ticks.
- Added validation for delay duration, cron expression, time zone, max tick,
  input type, and bounded capacity options.

## FluxFlow.Components.Timers 0.1.0-alpha.1

Initial reusable timer component package.

- Added `FluxFlow.Components.Timers` as a separate source project, test
  project, and package artifact.
- Added `timer.interval` with an `Output` port.
- Added `TimerTick` with timestamp, due time, sequence, elapsed time,
  interval, and drift metadata.
- Added interval, initial delay, immediate first tick, max tick, and bounded
  capacity options.
- Added package diagnostics for interval start, tick emission, stop, and
  failure.
- Added workflow events for emitted interval ticks.
- Added configuration validation for missing or invalid duration, capacity, and
  max tick options.

## FluxFlow.Components.Observability 0.1.0-alpha.1

Initial reusable observability component package.

- Added `FluxFlow.Components.Observability` as a separate source project, test
  project, and package artifact.
- Added `flow.logger` with `Input` and `Entries` ports.
- Added `flow.metrics` and `flow.counter` with `Input` and `Snapshots` ports.
- Added `FlowLogEntry`, `FlowMetricSnapshot`, and `FlowCounterSnapshot`
  contracts.
- Added host-registered input type aliases and value selectors.
- Added optional expression-backed counter predicates.
- Added stable observability error codes for selector and predicate failures.
- Added diagnostics with node name, input type, count, rate, log category,
  selected attributes, and failure metadata where available.

## FluxFlow.Components.FileSystem 0.4.0-alpha.1

Adds package-owned directory enumeration.

- Added `directory.enumerate` with an `Output` port.
- Added `DirectoryEnumerateEntry` and `DirectoryEntryType` contracts.
- Added directory, filter, recursive, file/directory inclusion, max entry,
  bounded capacity, base directory, and absolute path options.
- Added startup failures for missing directories, invalid directories, and
  denied absolute paths.
- Added configuration validation for disabled entry types and invalid limits.
- Added enumeration runtime errors as `FlowError` and node faults for access,
  IO, and unexpected directory traversal failures.
- Added diagnostics for enumeration start, entry emission, completion, and
  failure.

## FluxFlow.Components.FileSystem 0.3.0-alpha.1

Adds package-owned file system watching.

- Added `file.watch` with an `Output` port.
- Added `FileWatchEvent` and `FileWatchChangeType` contracts.
- Added directory, filter, subdirectory, notify filter, bounded capacity, base
  directory, and absolute path options.
- Added startup failures for missing directories, invalid directories, denied
  absolute paths, and unsupported notify filters.
- Added watcher runtime errors as `FlowError` without stopping later file
  events where the platform watcher can continue.
- Added diagnostics and workflow events for observed file changes.

## FluxFlow.Components.FileSystem 0.2.0-alpha.1

Adds package-owned file reads.

- Added `file.read` with `Input` and `Result` ports.
- Added `FileReadRequest`, `FileReadResult`, and `FileReadMode` contracts.
- Added text and byte read modes with per-request encoding for text reads.
- Added `maxBytes` protection for read nodes.
- Shared base directory, absolute path, and path escape handling between
  `file.write` and `file.read`.
- Added stable file read error codes for invalid paths, denied absolute paths,
  unsupported encodings, unsupported modes, access denial, IO failures, missing
  files, and oversized files.
- Added read diagnostics with path, resolved path, read mode, encoding, byte
  count, base directory, and max byte metadata where available.

## FluxFlow.Components.FileSystem 0.1.0-alpha.1

Initial reusable file system component package.

- Added `FluxFlow.Components.FileSystem` as a separate source project, test
  project, and package artifact.
- Added `file.write` with `Input` and `Result` ports.
- Added `FileWriteRequest`, `FileWriteResult`, and `FileWriteMode` contracts.
- Added base directory, absolute path, default encoding, and bounded capacity
  options.
- Added ordered asynchronous writes for overwrite, append, and create-new
  modes.
- Added stable file write error codes for invalid paths, denied absolute paths,
  missing content, unsupported encodings, unsupported modes, access denial, and
  IO failures.
- Added diagnostics with path, resolved path, mode, directory creation, base
  directory, and byte count metadata where available.

## FluxFlow.Components.Validation 0.1.0-alpha.1

Initial reusable validation component package.

- Added `FluxFlow.Components.Validation` as a separate source project, test
  project, and package artifact.
- Added `json.schema-validator` with `Input`, `Result`, `Valid`, and `Invalid`
  ports.
- Added inline schema and schema file loading options.
- Added host-registered input type aliases and value selectors.
- Routed invalid data to `Invalid` without reporting it as a processing error.
- Added stable validation error codes for schema loading, selector, conversion,
  and evaluation failures.
- Added diagnostics with input type, selector, schema id, schema path, validity,
  and issue count metadata where available.

## FluxFlow.Components.Control 0.1.0-alpha.1

Initial reusable control component package.

- Added `FluxFlow.Components.Control` as a separate source project, test
  project, and package artifact.
- Added `flow.filter` with `Input` and `Output` ports.
- Added `flow.when` with `Input`, `WhenTrue`, and `WhenFalse` ports.
- Added host-provided expression engine resolution, type aliases, and context
  factories.
- Added stable control error codes for expression evaluation failures.
- Added per-message expression failures as `FlowError` so later messages
  continue.
- Added diagnostics with input type, engine, expression id, expression name,
  route, and pass metadata where available.

## FluxFlow.Components.Mapping 0.1.0-alpha.1

Initial reusable mapping component package.

- Added `FluxFlow.Components.Mapping` as a separate source project, test
  project, and package artifact.
- Added `flow.mapper` with `Input` and `Output` ports.
- Added typed port support through package type aliases.
- Added host-provided expression engine resolution and context factories.
- Added per-message mapping failures as `FlowError` so later messages continue.
- Added mapping diagnostics with input type, output type, engine, expression id,
  and expression name metadata.

## FluxFlow.Components.Mqtt 0.2.1-alpha.1

MQTT package topic validation polish.

- Added public publish topic and subscription filter validation helpers.
- Validated publish topics before adapter calls.
- Validated default publish topics and subscription topic filters during node
  creation.
- Kept invalid topic handling on publish as `FlowError` so later messages can
  continue processing.

## FluxFlow.Components.Mqtt 0.2.0-alpha.1

MQTT package hardening for host integration.

- Added client factory context with node address, connection name, and profile.
- Added explicit client ownership through `MqttClientLease`.
- Added subscription leases so subscribe startup failures fail during startup.
- Added retained subscription options.
- Added richer publish and subscribe diagnostics/events with topic, payload
  size, quality setting, retain, and correlation metadata.
- Added split error codes for not-started, invalid topic, invalid payload,
  startup failure, and processing failure cases.

## FluxFlow.Components.Mqtt 0.1.0-alpha.1

Initial MQTT component package.

- Added `FluxFlow.Components.Mqtt` as a separate source project, test project,
  and package artifact.
- Added adapter-backed `mqtt.publish` and `mqtt.subscribe` nodes.
- Added typed MQTT request, result, received-message, options, and adapter
  contracts.
- Added MQTT module registration through `RegisterMqttComponents`.
- Added background subscribe lifecycle handling so long-lived subscriptions do
  not block runtime startup.
- Prepared independent package release support so this package can be published
  without republishing the engine.

## 0.5.0-alpha.1

Package authoring helpers.

- Added `FlowNodeRegistration` as a delegate-backed registration helper.
- Added `IFlowNodeModule` and `FlowNodeModule` for grouping component family
  registrations.
- Made range and module registration validate duplicate node types before
  mutating the registry.
- Added a neutral consumer sample that demonstrates workspace projection,
  explicit component registration, conditional links, events, and diagnostics.

## 0.4.0-alpha.1

Conditional link runtime behavior.

- Added runtime evaluation for link `when` expressions.
- Added `ExpressionFlowPredicate<TInput>` for expression-backed predicates.
- Allowed expression predicates to use custom `IFlowMapContextFactory<TInput>`
  implementations.
- Added an `OutputPort.TryLinkTo` overload that accepts an optional predicate.
- Kept existing unconditional link APIs working unchanged.

## 0.3.0-alpha.1

Neutral event metadata naming.

- Renamed `FlowEvent.Topic` to `FlowEvent.Channel`.
- Renamed the `EventFlowNodeBase.EmitEvent(... topic ...)` helper parameter
  to `channel`.
- Kept channel as first-class event metadata instead of moving it into
  attributes.

## 0.2.0-alpha.1

Engine-only prerelease boundary.

- Removed scenario/test definitions from `ApplicationDefinition`.
- Removed scenario validation from the engine definition validator.
- Removed scenario runner APIs from `FlowApplicationHost`.
- Kept runtime events and diagnostics as the generic observation surface.
- Documented that applications should project their own workspace models into
  executable engine resources and workflows.

## 0.1.0-alpha.1

Initial prerelease of `FluxFlow.Engine`.

- Protocol-neutral workflow definition model.
- Typed node input and output ports.
- Runtime graph builder with phase-ordered lifecycle.
- Reliable runtime fanout for linked inputs.
- Node authoring helpers for source, sink, transform, map, events, diagnostics,
  and registration.
- Runtime, workflow, host, event, error, and diagnostics streams.
- Generic scenario runner with event expectations.
- External expression adapter support.
- NuGet packaging with README, symbols, repository metadata, and MIT license.
