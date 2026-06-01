# Changelog

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

## FluxFlow.Components.Storage.Local 0.1.0-alpha.1

Initial file-backed local storage adapter package.

- Added `FluxFlow.Components.Storage.Local` as a separate source project, test
  project, and package artifact.
- Added `LocalStorageStore`, `LocalStorageStoreFactory`, local storage options,
  and `UseLocalStorage(...)` registration helpers.
- Persisted one JSON file per storage record under hashed store, collection,
  and key paths.
- Added support for create, replace, upsert, expected version checks,
  expiration-aware reads, and found/missing delete results.
- Added value size validation and best-effort temporary-file replacement on
  writes.
- Added focused coverage for persistence, write modes, version checks,
  expiration, deletes, path safety, size limits, factory defaults, and option
  validation.

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
- DynamicExpresso and JSONata expression adapters.
- NuGet packaging with README, symbols, repository metadata, and MIT license.
