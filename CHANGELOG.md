# Changelog

<!--
The 3.x line is the standalone-node era: every component is an engine-free node over the
FluxFlow.Nodes kit (FlowNode/FlowSource, the FlowMessage envelope + CorrelationId, broadcast
Output/Errors/Events). The optional engine runtime moves to 2.0.0; the new kit and support
packages debut at 1.0.0.
-->

## FluxFlow.Components.Designer 2.2.4

Hardens component design metadata port validation.

- `ComponentDesignMetadataValidator` now reports duplicate primary ports within
  the same direction, keeping Designer metadata to one preferred input and one
  preferred output.
- Adds validator coverage for duplicate primary input and output ports.

## FluxFlow.Components.Designer 2.2.3

Hardens component design metadata option validation.

- `ComponentDesignMetadataValidator` now validates option default values
  against the declared `OptionValueKind` for text-like, number, boolean,
  duration, and enum options.
- Enum defaults must be strings or enum values and must match a declared choice
  when choices are available.
- `Min` and `Max` metadata are now reported as invalid unless the option kind is
  number or duration.
- Adds validator coverage for accepted defaults, mismatched defaults, and
  invalid min/max usage.

## FluxFlow.Components.Designer 2.2.2

Hardens component design metadata catalog registration.

- `ComponentDesignMetadataCatalog.Add(...)` now stores a defensive snapshot of
  registered metadata after validation.
- Top-level metadata collections, nested option choices, and attribute maps are
  copied so later mutations to provider-owned collections cannot change catalog
  contents.
- Adds catalog snapshot coverage for options, choices, resources, ports, and
  attributes.

## FluxFlow.Components.RequestReply 1.1.3

Hardens request/reply option contracts.

- `RequestReplyOptions` now rejects unsupported modes, non-positive capacity,
  non-positive timeout, and non-positive sweep interval values when assigned.
- `CorrelatedRequestTrackerOptions` now rejects non-positive timeout and sweep
  interval values when assigned.
- Adds direct option contract tests while keeping coordinator and tracker
  constructor validation as defensive guardrails.

## FluxFlow.Components.Journal 2.0.3

Hardens journal retention option contracts.

- `JournalRetentionOptions.MaxRecords` now rejects negative values when
  assigned.
- `JournalRetentionOptions.MaxAge` now rejects zero or negative durations when
  assigned.
- Adds direct retention option tests while keeping cross-field validation, such
  as requiring `ReferenceTime` with `MaxAge`, in `InMemoryJournalStore`.

## FluxFlow.Components.Storage.FileSystem 3.1.4

Hardens file-system storage query expiration filtering.

- `FileSystemStorageStore.QueryAsync()` now captures one clock timestamp per
  query and uses it for every record match.
- Adds deterministic coverage with an advancing clock so one query cannot split
  near-expiration records across multiple effective timestamps.

## FluxFlow.Components.Storage.SqlFile 3.1.4

Hardens SQL-file storage query expiration filtering.

- `SqlFileStorageStore.QueryAsync()` now captures one clock timestamp per query
  and uses it for both SQL-side expiration filtering and in-memory matching.
- Adds deterministic coverage with an advancing clock so database filtering and
  attribute-aware in-memory filtering stay aligned.

## FluxFlow.Composition.Hosting 1.0.2

Hardens hosted resource reference lookup.

- `GetRequiredResourceKey`, `GetRequiredResource<TResource>`, and
  `GetResource<TResource>` now trim local resource slot names before reading
  node resource references.
- Configured keyed service references are trimmed before keyed DI lookup, so
  incidental configuration whitespace no longer changes resource identity.
- Adds hosted and direct resource-helper coverage for required and optional
  resource resolution through normalized keys.

## FluxFlow.Composition 1.0.4

Hardens composition reference parsing.

- `NodeReference.Parse` and `PortReference.Parse` now reject empty dotted
  segments instead of accepting malformed values such as `source..Output`.
- Assigned workflow, node, and port reference segments now trim surrounding
  whitespace, and blank workflow assignments are treated as absent.
- Adds direct reference parsing and formatting coverage for valid, trimmed, and
  malformed node and port references.

## FluxFlow.Nodes 1.1.2

Hardens node option validation.

- `FlowNodeOptions` now rejects non-positive input capacity and
  max-degree-of-parallelism values when assigned.
- `FlowSourceOptions` now rejects invalid output capacity values when assigned,
  while still allowing `UnboundedOutputCapacity`.
- Adds direct option contract tests for default values, valid source output
  capacity choices, and invalid capacity assignments.

## FluxFlow.Mapping 1.0.2

Hardens mapping adapter guardrails.

- `ExpressionFlowMapper<TInput,TOutput>` and `ExpressionFlowPredicate<TInput>`
  now fail at construction with a clear error when an expression engine returns
  a null compiled expression.
- Adds direct tests for delegate and expression mapper/predicate adapters,
  including compile-once expression behavior, default predicate context values,
  custom context factories, and public null guardrails.
- Corrects the public API overview so the internal compiled-expression wrapper
  is no longer listed as a public type.

## FluxFlow.Components.Expressions 2.0.3

Hardens context factory registry resolution.

- `FlowContextFactoryRegistry<TFactory>` now evaluates matching registrations
  as one set and returns the single most specific registration when one exists.
- Ambiguous context factory matches now produce deterministic diagnostics that
  list the matching registration types.
- Adds explicit public guardrail coverage for null default factories,
  registrations, and lookup input types.

## FluxFlow.Components.Configuration 1.1.5

Hardens configuration validation request contracts.

- `ConfigurationValidationRequest` now copies assigned resource and secret
  collections so caller list mutations cannot change what a constructed request
  represents.
- Updates `FluxFlow.Components.Configuration` request contracts without changing
  validation ownership.
- Null resource and secret collection assignments are still preserved so
  `ConfigurationValidator.ValidateAsync` can report structured invalid request
  diagnostics.

## FluxFlow.Components.Secrets 1.2.6

Hardens secret diagnostic metadata handling.

- `SecretDiagnostic` now copies assigned metadata so callers cannot mutate a
  diagnostic after construction by changing the source dictionary.
- Null diagnostic metadata assignments now produce an empty metadata map.
- Existing diagnostic string formatting remains metadata-safe.

## FluxFlow.Components.Resources 1.2.5

Hardens resource diagnostic metadata handling.

- `ResourceDiagnostic` now copies assigned metadata so callers cannot mutate a
  diagnostic after construction by changing the source dictionary.
- Null diagnostic metadata assignments now produce an empty metadata map.
- `ResourceDiagnostic.ToString()` now returns a metadata-safe one-line summary.

## FluxFlow.Components.Sources 3.1.1

Aligns source constructor validation with the standalone node conventions.

- `GeneratedSourceNode<TOutput>` and `SequenceSourceNode` now validate all
  construction options before their source pipelines are created.
- Invalid capacities, negative timing settings, generated loop/max item
  settings, sequence counts, and zero steps remain fail-fast construction
  errors.
- Runtime source lifecycle, timing, diagnostics, and correlation propagation
  are unchanged.

## FluxFlow.Components.Sources.Composition 1.3.1

Hardens config-bound source option diagnostics.

- Invalid generated and sequence source options now fail during composition
  build through the matching factories.
- Adds hosted composition coverage proving invalid timing, capacity,
  loop/max-item, count, and step values surface as factory diagnostics when
  build failures are configured as diagnostics.

## FluxFlow.Components.Sessions 3.1.5

Aligns Sessions constructor validation with the standalone node conventions.

- `SessionRecorderNode`, `SessionReplayNode`, and `SessionQueryNode` now
  resolve validated options and required stores before their node/source
  pipelines are created.
- Replay session id and pacing checks remain fail-fast construction errors.
- Runtime store ownership, replay pacing, query validation, diagnostics, and
  correlation propagation are unchanged.

## FluxFlow.Components.Sessions.Composition 1.3.1

Hardens config-bound Sessions option diagnostics.

- Invalid recorder, replay, and query options now fail during composition build
  through the matching factories.
- Adds hosted composition coverage proving invalid replay mode and existing
  numeric/query option failures surface as factory diagnostics when build
  failures are configured as diagnostics.

## FluxFlow.Components.FileSystem 3.1.1

Aligns FileSystem constructor option validation with the standalone node
conventions.

- `DirectoryEnumerateNode` and `FileWatchNode` now validate all construction
  options before their source pipelines are created.
- `FileReadNode` and `FileWriteNode` keep their existing fail-fast option
  behavior with simpler constructor paths.
- Runtime path-policy handling, file IO behavior, source lifecycle, events,
  errors, and correlation propagation are unchanged.

## FluxFlow.Components.FileSystem.Composition 1.3.1

Hardens config-bound FileSystem option diagnostics.

- Invalid read/write/default-encoding, directory enumeration, and file-watch
  options now fail during composition build through the matching factories.
- Adds hosted composition coverage proving invalid option values surface as
  factory diagnostics when build failures are configured as diagnostics.

## FluxFlow.Components.Timers 3.1.1

Aligns timer transform constructor option validation with the standalone node
conventions.

- `TimerDelayNode<TInput>`, `TimerThrottleNode<TInput>`, and
  `TimerDebounceNode<TInput>` now validate settings before their base node
  pipelines are created.
- Invalid duration settings and non-positive `BoundedCapacity` values now fail
  with timer-specific construction errors.
- Runtime timing behavior, correlation propagation, and source node behavior are
  unchanged.

## FluxFlow.Components.Timers.Composition 1.4.1

Hardens config-bound timer option diagnostics.

- Invalid timer settings now fail during composition build through the
  `timer.interval`, `timer.delay`, `timer.throttle`, and `timer.debounce`
  factories covered by this pass.
- Adds hosted composition coverage proving invalid transform durations and
  `boundedCapacity` values surface as factory diagnostics when build failures
  are configured as diagnostics.

## FluxFlow.Components.Validation 3.0.1

Aligns JSON schema validator constructor option validation with the standalone
node conventions.

- `JsonSchemaValidatorNode<TInput>` now validates `JsonSchemaValidatorOptions`
  before the base node pipeline is created.
- Blank `InputType` and non-positive `BoundedCapacity` values now fail with
  `json.schema-validator` construction errors.
- Runtime validation behavior, schema evaluation, and routing through
  `Valid`/`Invalid` are unchanged.

## FluxFlow.Components.Validation.Composition 1.2.1

Hardens config-bound validation option diagnostics.

- Invalid `JsonSchemaValidatorOptions` values now fail during composition build
  through the `json.schema-validator` factory.
- Adds hosted composition coverage proving invalid `inputType` and
  `boundedCapacity` values surface as factory diagnostics when build failures
  are configured as diagnostics.

## FluxFlow.Components.Routing 3.0.1

Aligns routing node constructor option validation with the standalone node
conventions.

- `FlowSwitchNode<TInput>`, `FlowForkNode<TInput>`,
  `FlowMergeNode<TInput>`, `FlowWindowNode<TInput>`, and
  `FlowCorrelationNode<TInput>` now validate options before the base node
  pipeline is created.
- Blank `InputType` and non-positive `BoundedCapacity` values now fail with
  routing-specific construction errors.
- Window and correlation-specific limits now fail fast with node-specific
  validation errors.

## FluxFlow.Components.Routing.Composition 1.2.1

Hardens config-bound routing option diagnostics.

- Invalid routing options now fail during composition build through the
  `flow.switch`, `flow.fork`, `flow.merge`, `flow.window`, and
  `flow.correlation` factories.
- Adds hosted composition coverage proving invalid `inputType`,
  `boundedCapacity`, window, and correlation values surface as factory
  diagnostics when build failures are configured as diagnostics.

## FluxFlow.Components.Observability 3.0.1

Aligns observability node constructor option validation with the standalone
node conventions.

- `FlowCounterNode<TInput>`, `FlowLoggerNode<TInput>`, and
  `FlowMetricsNode<TInput>` now validate options before the base node pipeline
  is created.
- Blank `InputType` and non-positive `BoundedCapacity` values now fail with
  observability-specific construction errors.
- Logger level validation remains fail-fast during construction.

## FluxFlow.Components.Observability.Composition 1.2.1

Hardens config-bound observability option diagnostics.

- Invalid observability options now fail during composition build through the
  `flow.counter`, `flow.logger`, and `flow.metrics` factories.
- Adds hosted composition coverage proving invalid `inputType` and
  `boundedCapacity` values surface as factory diagnostics when build failures
  are configured as diagnostics.

## FluxFlow.Components.Control 3.0.1

Aligns control node constructor option validation with the standalone node
conventions.

- Expression-backed `FilterNode<TInput>` and `WhenNode<TInput>` constructors
  now require a non-empty `Expression`.
- All control node constructors now reject blank `InputType` and non-positive
  `BoundedCapacity` values before the node pipeline is created.
- Compiled-predicate constructors remain available without an expression.

## FluxFlow.Components.Control.Composition 1.2.1

Hardens config-bound control option diagnostics.

- Invalid `ControlExpressionOptions` values now fail during composition build
  through the `flow.filter` and `flow.when` factories.
- Adds hosted composition coverage proving invalid `inputType` and
  `boundedCapacity` values surface as factory diagnostics when build failures
  are configured as diagnostics.

## FluxFlow.Components.Mapping 3.0.1

Hardens mapper option validation.

- `FlowMapperNode<TInput,TOutput>` now validates `MapperOptions` before the
  base node pipeline is created.
- Non-positive `BoundedCapacity` now fails with a mapper-specific construction
  error.
- Adds direct standalone mapper tests for invalid capacity.

## FluxFlow.Components.Mapping.Composition 1.2.1

Hardens config-bound mapper option diagnostics.

- Invalid `MapperOptions` values now fail during composition build through the
  `flow.mapper` factory.
- Adds hosted composition coverage proving invalid `boundedCapacity` surfaces
  as a factory diagnostic when build failures are configured as diagnostics.

## FluxFlow.Components.Http 3.0.1

Hardens HTTP client node option validation.

- `HttpClientNode` now rejects non-positive `BoundedCapacity`,
  `MaxResponseBodyBytes`, and `MaxDegreeOfParallelism` during construction.
- `DefaultTimeoutMilliseconds`, when configured, must now be greater than zero.
- Adds direct standalone node tests for invalid option values.

## FluxFlow.Components.Http.Composition 1.2.1

Hardens config-bound HTTP client option diagnostics.

- Invalid numeric `HttpClientNodeOptions` values now fail during composition
  build through the `http.client` factory.
- Adds hosted composition tests proving invalid config surfaces as factory
  diagnostics when build failures are configured as diagnostics.

## FluxFlow.Composition 1.0.3

Hardens runtime builder cancellation cleanup.

- `CompositionRuntimeBuilder.BuildAsync()` now disposes partially built nodes
  and links before rethrowing cancellation or unexpected build exceptions.
- Factory-thrown `OperationCanceledException` propagates when the build
  cancellation token is already canceled instead of becoming a factory
  diagnostic.
- Adds a regression test proving cancellation after node creation disposes the
  already-created node.

## FluxFlow.Composition 1.0.2

Hardens composed node cleanup.

- `ComposedNode.DisposeAsync()` now attempts the descriptor cleanup hook even
  when the wrapped node disposal path fails.
- If both node disposal and the descriptor cleanup hook fail, the failures are
  reported together as an aggregate exception.
- Adds direct composed-node disposal tests so adapter-owned cleanup hooks remain
  reliable during build failure cleanup and runtime disposal paths.

## FluxFlow.Composition.Hosting 1.0.1

Hardens hosted runtime lifecycle transitions.

- `CompositionRuntimeHost` now serializes build/start/stop transitions through
  the host lifecycle gate.
- Repeated hosted or manual start calls no longer start the same runtime more
  than once.
- Repeated stop calls no longer complete an already stopped runtime again.
- A runtime that has been stopped is not started again by the host.
- Adds direct hosting tests with a non-idempotent source to prove the host owns
  this lifecycle guarantee.

## FluxFlow.Composition 1.0.1

Hardens composition definition collection assignment.

- `CompositionDefinition`, `WorkflowDefinition`, and `NodeDefinition` now copy
  assigned dictionaries and lists so caller mutations cannot change a built
  definition through the original collection references.
- Definition dictionaries now normalize assigned collection comparers back to
  ordinal key comparison.
- Adds direct composition DTO tests for workflow, node, link, configuration,
  and resource collection copy behavior.

## FluxFlow.Nodes 1.1.1

Hardens node envelope and diagnostic metadata snapshots.

- `FlowMessage<T>.Headers` now copies assigned dictionaries so caller mutations
  cannot change an existing message envelope.
- `FlowEvent.Attributes` now copies assigned dictionaries so diagnostics remain
  stable after creation.
- Header and attribute snapshots use ordinal key comparison for deterministic
  lookup across hosts.

## FluxFlow.Mapping 1.0.1

Hardens mapping context variable snapshots.

- `FlowMapContext` now copies assigned variable dictionaries so caller
  mutations cannot change an existing per-message context.
- Assigned variables now use ordinal key comparison, keeping expression
  variable lookup deterministic across hosts.
- Adds direct `FluxFlow.Mapping` tests for context variable copy semantics.

## FluxFlow.Components.Expressions 2.0.2

Hardens expression engine registry name normalization.

- `FlowExpressionEngineRegistry` now trims resolver lookup names before
  invoking custom resolvers.
- Blank resolver lookup names are normalized to the default-engine `null`
  lookup, matching the built-in registry behavior.
- Unknown-engine diagnostics now report the normalized engine name.

## FluxFlow.Components.Storage.FileSystem 3.1.3

Hardens file-system storage backend options and lease caching.

- `FileSystemStorageStoreOptions` now trims root, store name, and default
  collection text when assigned.
- Invalid `MaxValueBytes` values are rejected at option assignment.
- Shared store leases now compare root paths with operating-system path
  case-sensitivity instead of uppercasing paths before caching.

## FluxFlow.Components.Storage.SqlFile 3.1.3

Hardens SQL-file storage backend options.

- `SqlFileStorageStoreOptions` now trims database path, store name, and default
  collection text when assigned.
- Invalid `MaxValueBytes` and `BusyTimeoutMilliseconds` values are rejected at
  option assignment.

## FluxFlow.Components.Journal 2.0.2

Hardens journal contract normalization.

- `JournalRecord`, `JournalEventInput`, and `JournalQuery` now trim text fields
  when assigned.
- Attribute maps are normalized and copied on assignment so caller mutations do
  not leak into records, event inputs, or queries.
- `JournalQueryResult` now copies assigned record collections so returned
  results remain stable after caller list mutations.
- `JournalQueryMatcher.Validate()` now owns shared query validation for
  invalid paging and inverted `From`/`To` time ranges, and the in-memory store
  uses the same validator before matching records.

## FluxFlow.Components.Configuration 1.1.4

Hardens configuration diagnostic contracts.

- Updates `FluxFlow.Components.Configuration` diagnostic and report contracts.
- `ConfigurationDiagnostic` now trims text fields and treats blank optional
  path/name/kind values as absent.
- Diagnostic metadata is copied on assignment so later caller mutations do not
  leak into diagnostics.
- `ConfigurationValidationReport` now copies assigned diagnostic collections
  and treats null assignments as an empty report.

## FluxFlow.Components.Secrets 1.2.5

Hardens secret collection diagnostics.

- Null secret records are now reported as structured `InvalidSecret`
  diagnostics at their collection index.
- Unresolved-secret and option-resolution batch helpers now tolerate null
  reference or option entries and return structured failed diagnostics.
- Duplicate-secret checks now ignore null entries instead of surfacing
  accidental null-reference failures.

## FluxFlow.Components.Resources 1.2.4

Hardens resource collection diagnostics.

- Null resource descriptors are now reported as structured `InvalidResource`
  diagnostics at their collection index.
- Missing-resource and unused-resource helpers now tolerate null reference or
  descriptor entries instead of surfacing accidental null-reference failures.

## FluxFlow.Components.Metrics 3.0.3

Hardens metrics option normalization and validation.

- `MetricsAggregateOptions.GroupByTag` now trims surrounding whitespace and
  treats blank values as absent.
- Invalid rate windows, bounded capacities, and group limits are rejected at
  option assignment.

## FluxFlow.Components.Metrics 3.0.2

Hardens metrics contract normalization.

- `MetricSampleInput`, `MetricSnapshotOutput`, and `MetricGroupSnapshot` now
  trim optional text values when assigned.
- Tags, latest samples, and group snapshot maps are copied with ordinal key
  comparison so later caller mutations do not leak into contracts.

## FluxFlow.Components.State 3.0.4

Hardens state reducer per-message operation validation.

- Unsupported `StateReducerInput.Operation` values now emit `InvalidMessage`
  errors instead of generic reducer failures.
- Later valid messages continue processing after an unsupported operation.

## FluxFlow.Components.State 3.0.3

Hardens state reducer option normalization and validation.

- `StateReducerOptions` now trims diagnostic text fields when assigned.
- Missing reducers, empty key expressions, non-positive bounded capacities, and
  negative max-key values are rejected at option assignment.

## FluxFlow.Components.State 3.0.2

Hardens state reducer contract normalization.

- `StateReducerInput` and `StateReducerResult` now trim key text when assigned.
- `StateReducerInput.Variables` is copied with ordinal key comparison so later
  caller mutations do not leak into the message contract.

## FluxFlow.Components.Sessions 3.1.4

Hardens session query response validation.

- `SessionQueryNode` now validates store-returned sessions against the
  normalized name, prefix, tag, date range, active/completed, and limit filters.
- Store results outside the query filter or above the requested limit now emit a
  `QueryFailed` error instead of being silently emitted or truncated.

## FluxFlow.Components.Sessions 3.1.3

Hardens session store response validation.

- `SessionRecorderNode`, `SessionReplayNode`, and `SessionQueryNode` now report
  clear failures when a host store returns null sessions, records, query
  results, or replay streams where `ISessionStore` requires a value.
- Recorder and query nodes keep later-message continuation for recoverable store
  contract failures.

## FluxFlow.Components.Sessions 3.1.2

Hardens session option normalization and validation.

- `SessionRecorderOptions`, `SessionReplayOptions`, and `SessionQueryOptions`
  now trim optional text and copy tag maps with ordinal key comparison.
- Invalid bounded capacities, query limits, replay ranges, replay modes, and
  timing values are rejected at option assignment.

## FluxFlow.Components.Sessions 3.1.1

Hardens session contract normalization.

- Session request, record, metadata, and query result contracts now trim optional
  text values and treat blank optional text as absent.
- Tag and attribute maps are copied with ordinal key comparison when assigned.
- Nested session/input contracts in append, complete, and query-result payloads
  are copied so later caller mutations do not leak into the contract object.

## FluxFlow.Components.Storage 3.0.9

Hardens storage store non-null result handling.

- `StoragePutNode`, `StorageQueryNode`, and `StorageDeleteNode` now report clear
  operation failures when an injected store returns a null result where the
  `IStorageStore` contract requires a value.
- `StorageQueryNode` now also reports null records inside returned query
  collections as `QueryFailed` errors.

## FluxFlow.Components.Storage 3.0.8

Hardens storage query response validation.

- `StorageQueryNode` now rejects store query results that contain records outside
  the normalized query filter.
- Stores that return more records than the requested limit are now reported as
  `QueryFailed` errors instead of being silently truncated.

## FluxFlow.Components.Storage 3.0.7

Hardens storage get response validation.

- `StorageGetNode` now rejects store records returned for a different
  collection or key.
- Mismatched get records are reported as `GetFailed` errors instead of being
  emitted on `Output` or `Found`.

## FluxFlow.Components.Storage 3.0.6

Hardens per-message storage write-mode validation.

- `StoragePutNode` now reports unsupported `StoragePutRequest.Mode` values as
  `InvalidRequest` errors.
- Invalid write-mode messages no longer reach the injected store.
- Later valid put messages continue processing normally.

## FluxFlow.Components.Storage.FileSystem 3.1.2

Hardens file-system storage write-mode validation.

- Direct `FileSystemStorageStore.PutAsync(...)` calls now reject unsupported
  `StoragePutRequest.Mode` values.
- Unsupported write modes are no longer treated as upserts.

## FluxFlow.Components.Storage.SqlFile 3.1.2

Hardens SQL-file storage write-mode validation.

- Direct `SqlFileStorageStore.PutAsync(...)` calls now reject unsupported
  `StoragePutRequest.Mode` values.
- Unsupported write modes are no longer treated as upserts.

## FluxFlow.Components.Storage 3.0.5

Hardens storage node option normalization and validation.

- `StoragePutOptions`, `StorageGetOptions`, `StorageQueryOptions`, and
  `StorageDeleteOptions` now trim default collections when assigned.
- Blank default collections are treated as absent.
- Non-positive bounded capacities are rejected at option assignment.
- `StorageQueryOptions` now rejects negative offsets and non-positive limits.
- `StoragePutOptions` now rejects unsupported write-mode values.

## FluxFlow.Components.Storage 3.0.4

Hardens storage output contract normalization.

- `StorageRecord`, `StorageResult`, and `StorageQueryResult` now trim textual
  identity and diagnostic fields when assigned.
- Blank optional content-type, message, and correlation values are treated as
  absent.
- Output attribute dictionaries are copied on assignment, use ordinal key
  comparison, and treat null as empty.
- `StorageQueryResult.Records` now copies assigned record lists so later caller
  mutations do not change the result.

## FluxFlow.Components.Storage 3.0.3

Hardens storage request contract normalization.

- `StoragePutRequest`, `StorageGetRequest`, `StorageQueryRequest`, and
  `StorageDeleteRequest` now trim optional text fields when assigned.
- Blank optional collection, key-prefix, content-type, and correlation values
  are treated as absent.
- Request attribute dictionaries are copied on assignment, use ordinal key
  comparison, and treat null as empty.
- Keeps required collection/key validation in nodes and stores so invalid
  workflow messages still surface as storage errors.

## FluxFlow.Components.Storage 3.0.2

Hardens storage store context normalization.

- `StorageStoreContext.StoreName` and `StorageStoreContext.Collection` now trim
  surrounding whitespace when assigned.
- Blank store names and collections are treated as absent before delegate-backed
  factories receive the context.
- Assigning a null `StorageStoreContext.Clock` now falls back to
  `TimeProvider.System`.
- Keeps store ownership, backend factories, storage nodes, and request
  contracts unchanged.

## FluxFlow.Components.RequestReply 1.1.2

Hardens request/reply coordinator option validation.

- `RequestReplyCoordinator<TRequest,TResponse>` now validates
  `RequestReplyOptions.Mode` before creating dataflow blocks.
- Capacity, timeout, and sweep interval validation now use one fail-fast
  coordinator options path.
- Invalid sweep intervals are rejected consistently, including fire-and-forget
  mode where no tracker is created.
- Keeps request correlation, fire-and-forget behavior, lifecycle completion,
  and transport ownership unchanged.

## FluxFlow.Components.Secrets 1.2.4

Hardens secret metadata and attribute map normalization.

- Secret descriptor metadata, secret reference attributes, and secret option
  metadata now trim valid keys and values when assigned.
- Duplicate map keys after trimming are reported as structured `InvalidSecret`
  diagnostics.
- Invalid maps are preserved for validation so null maps, blank keys, and blank
  values still produce diagnostics instead of construction failures.
- Keeps secret resolution, redaction, option resolution, and host-owned resolver
  behavior unchanged.

## FluxFlow.Components.Resources 1.2.3

Hardens resource metadata and attribute map normalization.

- Resource descriptor metadata and resource reference attributes now trim valid
  keys and values when assigned.
- Duplicate map keys after trimming are reported as structured
  `InvalidResource` diagnostics.
- Invalid maps are preserved for validation so null maps, blank keys, and blank
  values still produce diagnostics instead of construction failures.
- Keeps resource lookup, kind matching, and host-owned resource ownership
  unchanged.

## FluxFlow.Components.Configuration 1.1.3

Hardens configuration resource option metadata normalization.

- Updates `FluxFlow.Components.Configuration` validation contracts.
- Resource option metadata now trims valid keys and values when assigned.
- Duplicate metadata keys after trimming are reported as structured
  configuration diagnostics.
- Invalid maps are preserved for validation so null maps, blank keys, and blank
  values still produce diagnostics instead of construction failures.
- Keeps resource and secret lookup ownership unchanged.

## FluxFlow.Components.Secrets 1.2.3

Hardens secret scalar text normalization for config-bound callers.

- Secret descriptors and references now trim version, kind, display name, and
  summary fields when assigned.
- Secret option references and option resolution results now trim option paths.
- Secret resolution and duplicate detection now treat padded versions and kinds
  as the same logical values.
- Keeps secret values, redaction, metadata maps, and host-owned resolver
  behavior unchanged.

## FluxFlow.Components.Resources 1.2.2

Hardens resource scalar text normalization for config-bound callers.

- Resource descriptors and references now trim kind, display name, and summary
  fields when assigned.
- Resource lookup now treats padded kinds as the same logical kind.
- Keeps resource names, metadata maps, attributes, and host-owned resource
  ownership unchanged.

## FluxFlow.Components.Configuration 1.1.2

Hardens configuration option path normalization.

- Updates `FluxFlow.Components.Configuration` validation contracts.
- `ConfigurationResourceReference.Path` now trims surrounding whitespace when
  assigned.
- Resource diagnostics and option metadata now report the normalized option
  path.
- Keeps validation ownership, lookup behavior, and diagnostic codes unchanged.

## FluxFlow.Components.Secrets 1.2.2

Hardens secret name handling for config-bound callers.

- `SecretName` now trims surrounding whitespace at construction.
- Secret records and references whose names differ only by padding now resolve
  to the same logical name.
- Duplicate secret detection now catches padded forms of the same name.
- Keeps secret values, versions, kinds, redaction, and host-owned resolver
  behavior unchanged.

## FluxFlow.Components.Resources 1.2.1

Hardens resource name handling for config-bound callers.

- `ResourceName` now trims surrounding whitespace at construction.
- Resource descriptors and references whose names differ only by padding now
  resolve to the same logical name.
- Duplicate resource detection now catches padded forms of the same name.
- Keeps resource kinds, metadata, attributes, and host-owned resource ownership
  unchanged.

## FluxFlow.Components.Storage.FileSystem 3.1.1

Hardens file-system storage attribute handling.

- Normalizes attribute keys and values before persistence and query matching.
- Rejects blank attribute keys/values and duplicate attribute keys after
  trimming.
- Keeps file layout, value serialization, store factory sharing, and host-owned
  storage setup unchanged.

## FluxFlow.Components.Storage.SqlFile 3.1.1

Hardens SQL-file storage attribute handling.

- Normalizes attribute keys and values before persistence and query matching.
- Rejects blank attribute keys/values and duplicate attribute keys after
  trimming.
- Keeps database schema, value serialization, owned lease behavior, and
  host-owned storage setup unchanged.

## FluxFlow.Components.Journal 2.0.1

Hardens journal record normalization.

- Trims optional journal record and event-mapped text fields consistently.
- Normalizes journal attribute keys and values, rejects blank attribute values,
  and reports duplicate attribute keys after trimming.
- Keeps journal storage, query, retention, and runtime-neutral contracts
  unchanged.

## FluxFlow.Components.Configuration 1.1.1

Hardens configuration validation for null config-bound collections.

- Updates `FluxFlow.Components.Configuration` validation helpers.
- Reports null `Resources` and `Secrets` collections on
  `ConfigurationValidationRequest` as structured configuration diagnostics.
- Reports null resource or secret validation entries by index instead of
  throwing during validation.
- Keeps resource and secret lookup ownership unchanged.

## FluxFlow.Components.Storage 3.0.1

Hardens delegate-backed storage factory registration.

- `StorageComponentOptions.UseStore(...)` now rejects a null context before
  invoking the host delegate and reports a clear error when the delegate returns
  a null lease.
- `UseSharedStore(Func<StorageStoreContext,IStorageStore>)` now reports a clear
  error when the delegate returns a null store.
- Keeps store ownership and backend adapter behavior unchanged.

## FluxFlow.Components.Secrets 1.2.1

Hardens secret option batch resolution contracts.

- `SecretOptionResolver.ResolveAllAsync(...)` now validates the resolver
  argument before returning an empty result.
- Keeps secret resolution, redaction, and host-owned secret ownership
  unchanged.

## FluxFlow.Components.Resources 1.2.0

Hardens resource metadata validation for config-bound callers.

- Reports null descriptor metadata and reference attributes as structured
  `InvalidResource` diagnostics instead of throwing.
- Keeps resource lookup, duplicate detection, and host-owned resource ownership
  unchanged.

## FluxFlow.Components.Expressions 2.0.1

Fixes named-only expression engine registration.

- `FlowExpressionEngineRegistry.Use(engine, useAsDefault: false)` no longer
  makes the first registered engine the default fallback.
- Named resolution still works for named-only engines.
- Existing default engines remain unchanged when later named-only engines are
  registered.

## FluxFlow.Components.Designer 2.2.1

Hardens component design metadata validation for config-bound callers.

- Reports null top-level option, resource, port, and attribute collections as
  validation diagnostics instead of throwing.
- Reports null nested option choices, attributes, and list entries as validation
  diagnostics.
- Keeps the Designer contracts engine/composition neutral.

## FluxFlow.Components.RequestReply 1.1.1

Fixes request/reply coordinator lifecycle completion.

- `RequestReplyCoordinator<TRequest,TResponse>.Complete()` now closes both
  coordinator inputs, completes output/diagnostic ports, and fails in-flight
  callers with `OperationCanceledException`.
- `DisposeAsync()` now settles `Completion` instead of leaving awaiters blocked
  on the response input block.
- Keeps fault semantics unchanged: `Fault(exception)` still faults dataflow
  blocks and fails in-flight callers with the original exception.

## FluxFlow.Components.Secrets 1.2.0

Hardens secret metadata validation for config-bound callers.

- Reports null descriptor metadata, reference attributes, and option metadata as
  structured `InvalidSecret` diagnostics instead of throwing.
- Keeps secret resolution, redaction, and host-owned secret ownership unchanged.

## FluxFlow.Components.Configuration 1.1.0

Hardens configuration resource-option metadata validation.

- Updates `FluxFlow.Components.Configuration` validation helpers.
- Reports null, empty-key, and empty-value resource option metadata as
  structured `InvalidResourceReference` diagnostics.
- Keeps resource and secret lookup ownership unchanged; this package still only
  normalizes validation reports.

## FluxFlow.Components.Journal 2.0.0

Breaking support-package boundary cleanup.

- Removes the Journal package dependency on `FluxFlow.Engine`.
- Replaces the engine-specific `FlowEventJournalRecordMapper` API with neutral
  `JournalEventInput` and `JournalRecordMapper` contracts.
- Keeps journal records, queries, retention, `IJournalStore`, and
  `InMemoryJournalStore` behavior unchanged.

## FluxFlow.Components.Storage.FileSystem 3.1.0

Fixes context-aware file-system store factory sharing.

- Includes default collection and clock identity in the shared store cache key,
  preventing later leases for the same root and store name from inheriting the
  first opened context defaults.
- Keeps the shared-lease model for same-context opens so in-process optimistic
  concurrency remains coordinated by one store instance.
- Corrects file-system storage docs to use `StorageStoreContext.Collection` and
  describe shared factory leases.

## FluxFlow.Components.Storage.SqlFile 3.1.0

Clarifies SQL-file store context scoping and path policy.

- Adds regression coverage for rejecting absolute database paths when
  `AllowAbsoluteDatabasePath` is disabled.
- Verifies factory-opened stores keep context collection values ahead of option
  defaults while returning owned leases.
- Corrects SQL-file storage docs to use `StorageStoreContext.Collection` and
  document owned per-open leases.

## FluxFlow.Nodes 1.1.0

Adds bounded source-output support for standalone source nodes.

- Adds `FlowSourceOptions` with explicit output capacity configuration.
- Adds awaitable `FlowSource<TOutput>` emission so source loops can await
  bounded broadcast output acceptance.
- Keeps existing source behavior unbounded by default for sources that do not
  pass `FlowSourceOptions`.
- Updates loop-driven source components to wire their existing
  `BoundedCapacity` options into the shared source output contract.

## FluxFlow.Components.Sources 3.1.0

Wires source bounded-capacity options into the shared source-output contract.

- `source.generated` and `source.sequence` now pass `boundedCapacity` to
  `FluxFlow.Nodes` source output configuration.
- Loop-driven source emission awaits bounded broadcast output acceptance.
- Output remains broadcast/latest-wins; use a dedicated durable buffer when a
  workflow edge must guarantee no loss.
- Keeps item materialization, timing, fresh correlation ids, diagnostics, and
  standalone construction behavior unchanged.

## FluxFlow.Components.Timers 3.1.0

Wires timer source bounded-capacity options into the shared source-output
contract.

- `timer.interval` and `timer.schedule` now pass `boundedCapacity` to
  `FluxFlow.Nodes` source output configuration.
- Timer source loops await bounded broadcast output acceptance.
- Output remains broadcast/latest-wins; use a dedicated durable buffer when a
  workflow edge must guarantee no loss.
- Transform timer nodes keep their existing bounded input behavior.

## FluxFlow.Components.FileSystem 3.1.0

Wires file-system source bounded-capacity options into the shared source-output
contract.

- `directory.enumerate` and `file.watch` now pass `boundedCapacity` to
  `FluxFlow.Nodes` source output configuration.
- Directory enumeration awaits bounded broadcast output acceptance.
- Source outputs remain broadcast/latest-wins; use a dedicated durable buffer
  when a workflow edge must guarantee no loss.
- File watching keeps nonblocking watcher callbacks while using the configured
  bounded source output.

## FluxFlow.Components.Sessions 3.1.0

Wires session replay bounded-capacity options into the shared source-output
contract.

- `session.replay` now passes `boundedCapacity` to `FluxFlow.Nodes` source
  output configuration.
- Replay awaits bounded broadcast output acceptance while preserving store
  ownership, replay pacing, correlation, diagnostics, and standalone
  construction behavior.

## FluxFlow.Components.Mqtt 4.1.0

Wires MQTT trigger bounded-capacity options into the shared source-output
contract.

- `mqtt.trigger` now passes `boundedCapacity` to `FluxFlow.Nodes` bounded
  broadcast source output.
- Trigger receive processing awaits output-block acceptance before
  `OnEmit` acknowledgement.
- The same `boundedCapacity` option continues to bound the request/reply
  `Responses` target capacity.

## FluxFlow.Components.Mqtt.Composition 1.3.0

Aligns MQTT trigger composition packaging with the `FluxFlow.Components.Mqtt`
4.1.0 bounded source-output capacity support.

- Keeps composition registration APIs, resources, ports, and Designer metadata
  unchanged.
- Packages the updated trigger behavior for hosts using `mqtt.trigger` through
  `FluxFlow.Composition`.

## FluxFlow.Components.Sources.Composition 1.3.0

Aligns source composition packaging with the `FluxFlow.Components.Sources`
3.1.0 bounded source-output capacity support.

- Keeps composition registration APIs, resources, ports, and Designer metadata
  unchanged.
- Packages the updated `source.generated` and `source.sequence` source-output
  capacity behavior for composition hosts.

## FluxFlow.Components.Timers.Composition 1.4.0

Aligns timer composition packaging with the `FluxFlow.Components.Timers` 3.1.0
bounded source-output capacity support.

- Keeps composition registration APIs, resources, ports, and Designer metadata
  unchanged.
- Packages the updated `timer.interval` and `timer.schedule` source-output
  capacity behavior for composition hosts.

## FluxFlow.Components.FileSystem.Composition 1.3.0

Aligns file-system composition packaging with the
`FluxFlow.Components.FileSystem` 3.1.0 bounded source-output capacity support.

- Keeps composition registration APIs, resources, ports, path policy, and
  Designer metadata unchanged.
- Packages the updated `directory.enumerate` and `file.watch` source-output
  capacity behavior for composition hosts.

## FluxFlow.Components.Sessions.Composition 1.3.0

Aligns sessions composition packaging with the `FluxFlow.Components.Sessions`
3.1.0 bounded source-output capacity support.

- Keeps composition registration APIs, resources, ports, store ownership, and
  Designer metadata unchanged.
- Packages the updated `session.replay` source-output capacity behavior for
  composition hosts.

## FluxFlow.Components.Designer 2.2.0

Adds neutral resource metadata contracts for package-owned design metadata.

- Adds `ResourceDesignMetadata` and `ComponentDesignMetadata.Resources`.
- Validates resource names, duplicate resources, resource optional text, and
  resource attributes.
- Keeps resource metadata descriptive only; hosts still own resource
  registration, selection, lifetime, and disposal.

## FluxFlow.Components.Designer 2.1.0

Tightens Designer metadata validation for option choices.

- Requires enum options to define at least one choice.
- Rejects choice lists on non-enum options so provider metadata remains
  unambiguous for generated editors and documentation.

## FluxFlow.Components.Designer 2.0.0

Breaking Designer contract cleanup. `ComponentDesignMetadata.Type` now uses the
Designer-owned `ComponentType` value type, and `PortDesignMetadata.Name` now
uses `ComponentPortName`. The package no longer references `FluxFlow.Engine`,
so future component metadata providers can remain standalone-node-first and
runtime-neutral.

## FluxFlow.Composition 1.0.0

Adds the standalone-first composition layer for building linked node workflows
from fluent C# or `IConfiguration` JSON without depending on `FluxFlow.Engine`.
The package includes composition DTOs, explicit node factory registration, port
metadata, structural validation, direct typed Dataflow linking, runtime
lifecycle APIs, reload-facing contracts, and cleanup on build failure.

## FluxFlow.Composition.Hosting 1.0.0

Adds the optional hosting bridge for standalone compositions. The package
registers a composition runtime with `IServiceCollection`, loads definitions
from static objects or `IConfiguration`, builds and starts the runtime through
`IHostedService`, exposes build diagnostics through `ICompositionRuntimeHost`,
and provides keyed-resource helpers so node factories can resolve host or
adapter-owned resources from `NodeDefinition.Resources`.

## FluxFlow.Components.Mqtt 4.0.0

Breaking cleanup of the MQTT component boundary. The core package now exposes
client-library-neutral publish, trigger, health, received-context, and protocol metadata
contracts only. Publish and trigger nodes no longer own connection handles, factories,
profiles, reconnect policy, concrete client creation, or client lifetime. `MqttPublishOptions`
now contains only node runtime settings; publish topic, quality-of-service, retain flag,
payload, and MQTT protocol metadata live on `MqttPublishRequest`. Trigger request/reply uses
`CorrelatedRequestTracker` for pending correlation and timeout mechanics while acknowledgement
policy stays MQTT-owned. Concrete client-library integrations are split into separate adapter
packages.

## FluxFlow.Components.Mqtt.Composition 1.2.0

Adds Designer resource metadata for `mqtt.publish` and `mqtt.trigger`.

- Describes the required `publisher` resource for publish nodes.
- Describes the required `triggerSource` resource for trigger nodes.
- Describes the optional `clock` resource separately from editable MQTT node
  options.

## FluxFlow.Components.Mqtt.Composition 1.1.0

Adds package-owned Designer metadata for `mqtt.publish` and `mqtt.trigger`
composition nodes. The provider describes MQTT node options and fixed ports
while publisher, trigger source, and clock resources remain host-owned and
runtime behavior stays unchanged.

## FluxFlow.Components.Mqtt.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for MQTT publish and
trigger nodes. The package registers explicit `mqtt.publish` and `mqtt.trigger`
factories, binds existing MQTT node options from composition configuration, and
resolves adapter-owned keyed `IMqttPublisher` and `IMqttTriggerSource` resources
through `FluxFlow.Composition.Hosting`.

## FluxFlow.Components.RequestReply 1.1.0

Adds `CorrelatedRequestTracker<TContext, TResponse>` as a reusable pending-correlation and
timeout core for transport nodes that own their own ports and acknowledgement policy.
`RequestReplyCoordinator<TRequest, TResponse>` now uses the tracker internally without changing
its public coordinator behavior.

## FluxFlow.Components.Assertions 3.0.1

Aligns assertion node constructor option validation with the standalone node
family.

- Reports missing `expression` and empty `inputType` as `ArgumentException`.
- Reports non-positive `boundedCapacity` as `ArgumentOutOfRangeException`.
- Leaves runtime assertion evaluation, routing, diagnostics, and error-port
  behavior unchanged.

## FluxFlow.Components.Assertions 3.0.0

Engine-free standalone rewrite over the FluxFlow.Nodes kit. The assertion node is a
`FlowNode` taking its options/expression engine and `TimeProvider` directly; results fan out
to Result/Passed/Failed ports via `AddOutput`; correlation flows on `FlowMessage`. The engine
factory/module/registration/design-metadata glue is removed.

## FluxFlow.Components.Assertions.Composition 1.2.0

Adds Designer resource metadata for `flow.assert`.

- Describes the required `engine` resource separately from editable assertion
  options.
- Describes optional `contextFactory` and `clock` resources for host-owned
  context customization and deterministic diagnostics.

## FluxFlow.Components.Assertions.Composition 1.1.0

Adds package-owned Designer metadata for the `flow.assert` composition node.
The provider describes assertion options and ports while runtime resources
remain host-owned and assertion behavior stays unchanged.

## FluxFlow.Components.Assertions.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for closed generic
assertion nodes. The package registers explicit `flow.assert` factories, binds
`AssertionOptions` from composition configuration, and resolves keyed
`IFlowExpressionEngine`, `IFlowMapContextFactory<TInput>`, and `TimeProvider`
resources through `FluxFlow.Composition.Hosting`.

## FluxFlow.Components.Control 3.0.0

Engine-free standalone rewrite. Filter and When are `FlowNode`s over the kit (When fans out to
WhenTrue/WhenFalse via `AddOutput`); the predicate compiles once at construction against an
injected expression engine (from FluxFlow.Mapping). Engine glue removed.

## FluxFlow.Components.Control.Composition 1.2.0

Adds Designer resource metadata for `flow.filter` and `flow.when`.

- Describes the required `engine` resource separately from editable control
  options.
- Describes optional `contextFactory` and `clock` resources for host-owned
  context customization and deterministic diagnostics.

## FluxFlow.Components.Control.Composition 1.1.0

Adds package-owned Designer metadata for the `flow.filter` and `flow.when`
composition nodes. The provider describes control options and ports while
runtime resources remain host-owned and control behavior stays unchanged.

## FluxFlow.Components.Control.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for closed generic
control nodes. The package registers explicit `flow.filter` and `flow.when`
factories, binds `ControlExpressionOptions` from composition configuration, and
resolves keyed `IFlowExpressionEngine`, `IFlowMapContextFactory<TInput>`, and
`TimeProvider` resources through `FluxFlow.Composition.Hosting`.

## FluxFlow.Components.Expectations 3.0.1

Aligns event expectation constructor option validation with the standalone node
family.

- Reports invalid `timeoutMilliseconds`, `maxObservedEvents`,
  `maxPreviewChars`, and `boundedCapacity` as
  `ArgumentOutOfRangeException`.
- Leaves expectation matching, timeout resolution, diagnostics, and error-port
  behavior unchanged.

## FluxFlow.Components.Expectations 3.0.0

Engine-free standalone rewrite. The expectation node is a `FlowNode` with a deterministic
timeout armed against an injected `TimeProvider`. Engine glue removed.

## FluxFlow.Components.Expectations.Composition 1.2.0

Adds Designer resource metadata for `event.expectation`.

- Describes the optional `clock` resource separately from editable expectation
  options.
- Keeps runtime behavior unchanged; hosts still own keyed clock registration
  and lifetime.

## FluxFlow.Components.Expectations.Composition 1.1.0

Adds package-owned Designer metadata for the `event.expectation` composition
node. The provider describes event expectation options and fixed
request/result ports while `clock` remains a host-owned resource and runtime
behavior stays unchanged.

## FluxFlow.Components.Expectations.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for the standalone
event expectation node. The package registers the explicit `event.expectation`
factory, binds `EventExpectationOptions` from composition configuration, and
resolves optional keyed `TimeProvider` resources through
`FluxFlow.Composition.Hosting`.

## FluxFlow.Components.FileSystem 3.0.1

Aligns file-system constructor option validation with the standalone node
family.

- Reports invalid `boundedCapacity` with FileSystem node option names for
  `file.read`, `file.write`, `directory.enumerate`, and `file.watch`.
- Ensures `file.read` validates `maxBytes` before base-node setup.
- Leaves file reading, writing, enumeration, watching, path policy, diagnostics,
  and error-port behavior unchanged for valid options.

## FluxFlow.Components.FileSystem 3.0.0

Engine-free standalone rewrite. Read/write are `FlowNode`s; directory-enumerate and file-watch
are `FlowSource`s (the watcher is disposed in `OnDisposeAsync`). Engine glue removed.

## FluxFlow.Components.FileSystem.Composition 1.2.0

Adds Designer resource metadata for file-system composition nodes.

- Describes the optional `clock` resource separately from editable file-system
  options.
- Keeps runtime behavior unchanged; hosts still own keyed clock registration
  and lifetime.

## FluxFlow.Components.FileSystem.Composition 1.1.0

Adds package-owned Designer metadata for `file.read`, `file.write`,
`directory.enumerate`, and `file.watch` composition nodes. The provider
describes file-system options and fixed ports while path policy remains node
configuration, `clock` remains a host-owned resource, and runtime behavior
stays unchanged.

## FluxFlow.Components.FileSystem.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for standalone file
system nodes. The package registers explicit `file.read`, `file.write`,
`directory.enumerate`, and `file.watch` factories, binds existing file system
options from composition configuration, and resolves optional keyed
`TimeProvider` resources through `FluxFlow.Composition.Hosting`.

## FluxFlow.Components.Http 3.0.0

Engine-free standalone rewrite to a single `HttpClientNode : FlowNode<HttpRequestInput,
HttpResponseOutput>` over an injected `HttpClient` — the connection-resource node, sender
factory, and in-node SSRF guard are gone (transport policy lives on the injected client).

## FluxFlow.Components.Http.Composition 1.2.0

Adds Designer resource metadata for `http.client`.

- Describes the required `client` resource separately from editable HTTP client
  node options.
- Describes the optional `clock` resource for deterministic diagnostics and
  timeout behavior.

## FluxFlow.Components.Http.Composition 1.1.0

Adds package-owned Designer metadata for the `http.client` composition node.
The provider describes HTTP client options and fixed request/result ports while
`HttpClient` instances and clocks remain host-owned resources and runtime
behavior stays unchanged.

## FluxFlow.Components.Http.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for the standalone
HTTP client node. The package registers the explicit `http.client` factory,
binds `HttpClientNodeOptions` from composition configuration, and resolves a
host-owned keyed `HttpClient` resource through `FluxFlow.Composition.Hosting`.

## FluxFlow.Components.Mapping 3.0.0

Engine-free standalone rewrite. The mapper node is a `FlowNode` with the primary result on
Output and failures on a Failed port (`AddOutput`); the mapping expression compiles once.
Engine glue removed.

## FluxFlow.Components.Mapping.Composition 1.2.0

Adds Designer resource metadata for `flow.mapper`.

- Describes the required `engine` resource and optional `contextFactory` and
  `clock` resources separately from editable mapper options.
- Keeps runtime behavior unchanged; hosts still own keyed resource
  registration and lifetime.

## FluxFlow.Components.Mapping.Composition 1.1.0

Adds package-owned Designer metadata for the `flow.mapper` composition node.
The provider describes mapper options and ports while runtime resources remain
host-owned and mapper behavior stays unchanged.

## FluxFlow.Components.Mapping.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for closed generic
mapper nodes. The package registers explicit `flow.mapper` factories, binds
`MapperOptions` from composition configuration, and resolves keyed
`IFlowExpressionEngine`, `IMappingContextFactory`, and `TimeProvider` resources
through `FluxFlow.Composition.Hosting`.

## FluxFlow.Components.Metrics 3.0.1

Aligns metrics aggregate constructor option validation with the standalone node
family.

- Reports invalid `rateWindowSeconds`, `boundedCapacity`, and `maxGroups` as
  `ArgumentOutOfRangeException` with metrics option names.
- Rejects non-finite rate windows before rate calculations are configured.
- Leaves aggregation, grouping, diagnostics, and error-port behavior unchanged
  for valid options.

## FluxFlow.Components.Metrics 3.0.0

Engine-free standalone rewrite. The aggregate node is a `FlowNode`; in coalesce mode the single
final snapshot is emitted from the kit's `OnInputCompletedAsync` drain hook (deterministic, no
dropped snapshot under load). Engine glue removed.

## FluxFlow.Components.Mqtt 3.0.0

Engine-free standalone rewrite over small client-library-neutral MQTT contracts. Publish is a
`FlowNode` over `IMqttPublisher`; trigger is a `FlowSource` over `IMqttTriggerSource` and streams
`IMqttReceivedContext` values for adapter-owned acknowledgement. Connection handles, client
factories, profiles, reconnect policy, connection nodes, and concrete client creation are removed
from the core package. Trigger request/reply now uses the shared correlated request tracker.

## FluxFlow.Components.Mqtt.MqttNet 1.1.0

Adds adapter-local DI registration through `AddFluxFlowMqttClient`, keyed
registrations for `MqttNetClient`, `IMqttPublisher`, `IMqttTriggerSource`, and
`IMqttClientHealthSource`, plus optional hosted connect/disconnect lifetime
through `MqttClientRegistrationOptions`. Hosted connect/disconnect is opt-in
with `ConnectWithHost = true`; the default registration only creates the keyed
client and MQTT role services.

## FluxFlow.Components.Mqtt.MqttNet 1.0.0

Initial MQTTnet-backed adapter package for FluxFlow MQTT components. `MqttNetClient` owns MQTT
client creation, broker connection, Last Will setup, reconnect, publish mapping, trigger
subscriptions, manual acknowledgement hooks, and health events while implementing the neutral
`IMqttPublisher`, `IMqttTriggerSource`, and `IMqttClientHealthSource` contracts.

## FluxFlow.Components.Mqtt.PulseMqtt 1.1.0

Updates the adapter to Pulse MQTT `2.5.0`, uses the Pulse MQTT-named lifecycle
APIs internally, and maps FluxFlow manual trigger acknowledgement modes to Pulse
acknowledged route streams. `Acknowledgement.None` keeps managed route-stream
acknowledgement; `OnEmit` and `OnSuccessfulResponse` now delegate `AckAsync` /
`NackAsync` to the Pulse message context. Manual acknowledged Pulse routes are
single-owner for each matching publish, matching Pulse MQTT's acknowledged-route
contract.

Adds adapter-local DI registration through `AddFluxFlowMqttClient`, keyed
registrations for `PulseMqttClient`, `IMqttPublisher`, `IMqttTriggerSource`,
and `IMqttClientHealthSource`, plus optional hosted client startup. Also exposes
optional Pulse durable message/session store hooks while keeping offline queueing
opt-in.

## FluxFlow.Components.Mqtt.PulseMqtt 1.0.0

Initial Pulse MQTT-backed adapter package for FluxFlow MQTT components. `PulseMqttClient` owns
Pulse client creation, transport configuration, start/stop, Last Will setup, publish mapping,
route-stream trigger subscriptions, and health events while implementing the neutral
`IMqttPublisher`, `IMqttTriggerSource`, and `IMqttClientHealthSource` contracts. Manual broker
acknowledgement modes are rejected because Pulse route streams manage acknowledgement internally.
Targets Pulse MQTT `2.0.0`, using explicit broker subscriptions plus local route streams.

## FluxFlow.Components.Observability 3.0.0

Engine-free standalone rewrite. Logger/counter/metrics are `FlowNode` transforms over the kit.
Engine glue removed.

## FluxFlow.Components.Observability.Composition 1.2.0

Adds Designer resource metadata for observability composition nodes.

- Describes the counter `engine`, `contextFactory`, and `clock` resources,
  including the conditional engine requirement for predicate/expression
  configuration.
- Describes logger `clock` plus the dynamic `attribute:{name}` selector
  resource pattern.
- Describes metrics `sizeSelector` and `clock` resources separately from
  editable observability node options.

## FluxFlow.Components.Observability.Composition 1.1.0

Adds package-owned Designer metadata for `flow.counter`, `flow.logger`, and
`flow.metrics` composition nodes. The provider describes observability options
and fixed request/result ports while expression engines, selectors, context
factories, and clocks remain host-owned resources and runtime behavior stays
unchanged.

## FluxFlow.Components.Observability.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for standalone
observability nodes. The package registers explicit `flow.counter`,
`flow.logger`, and `flow.metrics` factories, binds the existing observability
options from composition configuration, and resolves host-owned keyed
expression, selector, context, and clock resources.

## FluxFlow.Components.Metrics.Composition 1.2.0

Adds Designer resource metadata for `metrics.aggregate`.

- Describes the optional `clock` resource separately from editable metrics
  options.
- Keeps runtime behavior unchanged; hosts still own keyed clock registration
  and lifetime.

## FluxFlow.Components.Metrics.Composition 1.1.0

Adds package-owned Designer metadata for the `metrics.aggregate` composition
node. The provider describes metrics aggregate options and fixed request/result
ports while `clock` remains a host-owned resource and runtime behavior stays
unchanged.

## FluxFlow.Components.Metrics.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for the standalone
metrics aggregate node. The package registers the explicit `metrics.aggregate`
factory, binds `MetricsAggregateOptions` from composition configuration, and
resolves optional keyed `TimeProvider` resources through
`FluxFlow.Composition.Hosting`.

## FluxFlow.Components.Payloads 3.0.0

Engine-free standalone rewrite. The inspect node is a `FlowNode` over the kit. Engine glue
removed.

## FluxFlow.Components.Payloads.Composition 1.2.0

Adds Designer resource metadata for `payload.inspect`.

- Describes the optional `clock` resource separately from editable payload
  inspection options.
- Keeps runtime behavior unchanged; hosts still own keyed clock registration
  and lifetime.

## FluxFlow.Components.Payloads.Composition 1.1.0

Adds package-owned Designer metadata for the `payload.inspect` composition
node. The provider describes payload inspection options and fixed
request/result ports while `clock` remains a host-owned resource and runtime
behavior stays unchanged.

## FluxFlow.Components.Payloads.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for the standalone
payload inspection node. The package registers the explicit `payload.inspect`
factory, binds `PayloadInspectOptions` from composition configuration, and
resolves optional keyed `TimeProvider` resources through
`FluxFlow.Composition.Hosting`.

## FluxFlow.Components.Projections 3.0.1

Aligns event projection constructor option validation with the standalone node
family.

- Reports non-positive `boundedCapacity` as `ArgumentOutOfRangeException` with
  the projection option name.
- Leaves projection counting, rate calculation, diagnostics, and error-port
  behavior unchanged.

## FluxFlow.Components.Projections 3.0.0

Engine-free standalone rewrite. The event-projection node is a `FlowNode` over a
package-owned `ProjectionEvent` contract. Engine glue removed.

## FluxFlow.Components.Projections.Composition 1.2.0

Adds Designer resource metadata for `event.projection`.

- Describes the optional `clock` resource separately from editable projection
  options.
- Keeps runtime behavior unchanged; hosts still own keyed clock registration
  and lifetime.

## FluxFlow.Components.Projections.Composition 1.1.0

Adds package-owned Designer metadata for the `event.projection` composition
node. The provider describes event projection options and fixed
request/result ports while `clock` remains a host-owned resource and runtime
behavior stays unchanged.

## FluxFlow.Components.Projections.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for the standalone
event projection node. The package registers the explicit `event.projection`
factory, binds `EventProjectionOptions` from composition configuration, and
resolves optional keyed `TimeProvider` resources through
`FluxFlow.Composition.Hosting`.

## FluxFlow.Components.Routing 3.0.0

Engine-free standalone rewrite. Switch/Fork/Correlation/Window are `FlowNode`s (multi-output via
`AddOutput`), Merge fans in to a single Input, and Join is a self-contained two-input node over
the kit's envelope/error/event primitives. Engine glue removed.

## FluxFlow.Components.Routing.Composition 1.2.0

Adds Designer resource metadata for routing composition nodes.

- Describes selector delegate resources required by `flow.switch`,
  `flow.correlation`, and `flow.join`.
- Describes the optional `clock` resource for all routing node factories.
- Keeps dynamic switch/fork output options as editable configuration because
  those ports are created after factory option binding.

## FluxFlow.Components.Routing.Composition 1.1.0

Adds package-owned Designer metadata for `flow.switch`, `flow.fork`,
`flow.merge`, `flow.window`, `flow.correlation`, and `flow.join` composition
nodes. The provider describes routing options and built-in ports while selector
delegates and `clock` remain host-owned resources and runtime behavior stays
unchanged.

## FluxFlow.Components.Routing.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for standalone routing
nodes. The package registers explicit `flow.switch`, `flow.fork`,
`flow.merge`, `flow.window`, `flow.correlation`, and `flow.join` factories,
binds existing routing options from composition configuration, and resolves
host-owned keyed selector delegates plus optional keyed `TimeProvider`
resources through `FluxFlow.Composition.Hosting`.

## FluxFlow.Components.Serialization 3.0.0

Engine-free standalone rewrite. The serialize/deserialize transforms are `FlowNode`s over the
kit. Engine glue removed.

## FluxFlow.Components.Serialization.Composition 1.2.0

Adds Designer resource metadata for serialization composition nodes.

- Describes the optional `clock` resource separately from editable
  serialization options.
- Keeps runtime behavior unchanged; hosts still own keyed clock registration
  and lifetime.

## FluxFlow.Components.Serialization.Composition 1.1.0

Adds package-owned Designer metadata for `json.parse`, `json.stringify`,
`text.encode`, `text.decode`, `base64.encode`, and `base64.decode` composition
nodes. The provider describes serialization options and fixed request/result
ports while `clock` remains a host-owned resource and runtime behavior stays
unchanged.

## FluxFlow.Components.Serialization.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for standalone
serialization nodes. The package registers explicit `json.parse`,
`json.stringify`, `text.encode`, `text.decode`, `base64.encode`, and
`base64.decode` factories, binds existing serialization options from
composition configuration, and resolves optional keyed `TimeProvider` resources
through `FluxFlow.Composition.Hosting`.

## FluxFlow.Components.Sessions 3.0.0

Engine-free standalone rewrite. Recorder is a `FlowNode`, replay is a `FlowSource` (paced by an
injected `TimeProvider`), query fans out via `AddOutput`. A mid-stream store failure reports
`ReplayFailed` before faulting. Engine glue removed.

## FluxFlow.Components.Sessions.Composition 1.2.0

Adds Designer resource metadata for session composition nodes.

- Describes the required `store` resource and optional `clock` resource
  separately from editable session options.
- Keeps runtime behavior unchanged; hosts still own keyed session store and
  clock registration and lifetime.

## FluxFlow.Components.Sessions.Composition 1.1.0

Adds package-owned Designer metadata for `session.recorder`, `session.replay`,
and `session.query` composition nodes. The provider describes session options
and fixed ports while session stores and `clock` remain host-owned resources and
runtime behavior stays unchanged.

## FluxFlow.Components.Sessions.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for standalone
session nodes. The package registers explicit `session.recorder`,
`session.replay`, and `session.query` factories, binds existing session options
from composition configuration, and resolves keyed `ISessionStore` plus
optional keyed `TimeProvider` resources through `FluxFlow.Composition.Hosting`.

## FluxFlow.Components.Sources 3.0.0

Engine-free standalone rewrite. Generated and sequence sources are `FlowSource`s; the host
supplies the items directly (the JSON + type-alias reader layer is gone). Engine glue removed.

## FluxFlow.Components.Sources.Composition 1.2.0

Adds Designer resource metadata for `source.generated` and `source.sequence`.

- Describes the optional `clock` resource separately from editable source
  options.
- Keeps runtime behavior unchanged; hosts still own keyed clock registration
  and lifetime.

## FluxFlow.Components.Sources.Composition 1.1.0

Adds package-owned Designer metadata for `source.generated` and
`source.sequence` composition nodes. The provider describes source options and
ports, including inline generated `items`, while `clock` remains a host-owned
resource and runtime behavior stays unchanged.

## FluxFlow.Components.Sources.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for standalone source
nodes. The package registers explicit `source.generated` and `source.sequence`
factories, binds existing source options from composition configuration,
deserializes inline generated `items` into closed generic output types, and
resolves optional keyed `TimeProvider` resources through
`FluxFlow.Composition.Hosting`.

## FluxFlow.Components.State 3.0.1

Aligns state reducer constructor option validation with the standalone node
family.

- Reports missing `reducer` and empty `keyExpression` as `ArgumentException`.
- Reports non-positive `boundedCapacity` and negative `maxKeys` as
  `ArgumentOutOfRangeException`.
- Leaves reducer execution, state updates, diagnostics, and error-port behavior
  unchanged.

## FluxFlow.Components.State 3.0.0

Engine-free standalone rewrite. The reducer node is a `FlowNode` over the kit, timed against an
injected `TimeProvider`. Engine glue removed.

## FluxFlow.Components.State.Composition 1.2.0

Adds Designer resource metadata for `state.reducer`.

- Describes the required `engine` resource and optional `clock` resource
  separately from editable reducer options.
- Keeps runtime behavior unchanged; hosts still own keyed expression engine and
  clock registration and lifetime.

## FluxFlow.Components.State.Composition 1.1.0

Adds package-owned Designer metadata for the `state.reducer` composition node.
The provider describes reducer options and fixed request/result ports while the
expression engine and `clock` remain host-owned resources and runtime behavior
stays unchanged.

## FluxFlow.Components.State.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for the standalone
state reducer node. The package registers the explicit `state.reducer` factory,
binds `StateReducerOptions` from composition configuration, and resolves a
keyed `IFlowExpressionEngine` plus optional keyed `TimeProvider` resources
through `FluxFlow.Composition.Hosting`.

## FluxFlow.Components.Storage 3.0.0

Engine-free standalone rewrite. Put/get/query/delete are `FlowNode`s taking an injected
`IStorageStore` (the host owns the store lifetime, like `HttpClient`); the in-graph
storage-connection resource node is removed. Get/query fan out via `AddOutput`. The
`IStorageStore` contract + store context/lease/factory are preserved for the adapters.

## FluxFlow.Components.Storage.Composition 1.2.0

Adds Designer resource metadata for storage composition nodes.

- Describes the required `store` resource and optional `clock` resource
  separately from editable storage options.
- Keeps runtime behavior unchanged; hosts still own keyed storage store and
  clock registration and lifetime.

## FluxFlow.Components.Storage.Composition 1.1.0

Adds package-owned Designer metadata for `storage.put`, `storage.get`,
`storage.query`, and `storage.delete` composition nodes. The provider describes
storage options and fixed ports while concrete stores and `clock` remain
host-owned resources and runtime behavior stays unchanged.

## FluxFlow.Components.Storage.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for standalone
storage nodes. The package registers explicit `storage.put`, `storage.get`,
`storage.query`, and `storage.delete` factories, binds existing storage options
from composition configuration, and resolves keyed `IStorageStore` plus
optional keyed `TimeProvider` resources through `FluxFlow.Composition.Hosting`.

## FluxFlow.Components.Storage.FileSystem 3.0.0

Rebuilt against FluxFlow.Components.Storage 3.0.0 (engine-free). The file-system `IStorageStore`
adapter is unchanged in behavior. Documentation now shows the backend adapter
as a host-owned `IStorageStore` source for `FluxFlow.Components.Storage.Composition`,
not as a workflow node composition package.

## FluxFlow.Components.Storage.SqlFile 3.0.0

Rebuilt against FluxFlow.Components.Storage 3.0.0 (engine-free). The SQL-file `IStorageStore`
adapter is unchanged in behavior. Documentation now shows the backend adapter
as a host-owned `IStorageStore` source for `FluxFlow.Components.Storage.Composition`,
not as a workflow node composition package.

## FluxFlow.Components.Timers 3.0.0

Engine-free standalone rewrite. Interval/schedule are `FlowSource`s; delay/throttle/debounce are
`FlowNode`s, all timed against an injected `TimeProvider`. Delay preserves constant-offset-from-
arrival burst semantics; debounce flushes its pending item via the drain hook. Engine glue
removed.

## FluxFlow.Components.Timers.Composition 1.3.0

Adds explicit Designer metadata for the intentionally omitted schedule
`timeZone` option.

- Marks `timeZone` as omitted from editable schedule metadata because
  `TimerScheduleSettings.TimeZone` requires typed `TimeZoneInfo`
  configuration.
- Keeps runtime behavior unchanged; the composition adapter still does not add
  time-zone id conversion.

## FluxFlow.Components.Timers.Composition 1.2.0

Adds Designer resource metadata for timer composition nodes.

- Describes the optional `clock` resource separately from editable timer
  options.
- Keeps runtime behavior unchanged; hosts still own keyed clock registration
  and lifetime.

## FluxFlow.Components.Timers.Composition 1.1.0

Adds package-owned Designer metadata for `timer.interval`, `timer.schedule`,
`timer.delay`, `timer.throttle`, and `timer.debounce` composition nodes. The
provider describes timer options and ports while `clock` remains a host-owned
resource and runtime behavior stays unchanged.

## FluxFlow.Components.Timers.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for all standalone
timer nodes. The package registers explicit `timer.interval`, `timer.schedule`,
`timer.delay`, `timer.throttle`, and `timer.debounce` factories, binds existing
timer settings from composition configuration, and resolves optional keyed
`TimeProvider` resources through `FluxFlow.Composition.Hosting`.

## FluxFlow.Components.Validation 3.0.0

Engine-free standalone rewrite. The JSON-schema validator is a `FlowNode` with Valid/Invalid
fan-out via `AddOutput`. Engine glue removed.

## FluxFlow.Components.Validation.Composition 1.2.0

Adds Designer resource metadata for `json.schema-validator`.

- Describes optional `selector` and `clock` resources separately from editable
  validation options.
- Keeps runtime behavior unchanged; hosts still own keyed selector and clock
  registration and lifetime.

## FluxFlow.Components.Validation.Composition 1.1.0

Adds package-owned Designer metadata for the `json.schema-validator`
composition node. The provider describes validator options and ports while
runtime resources remain host-owned and validation behavior stays unchanged.

## FluxFlow.Components.Validation.Composition 1.0.0

Adds optional `FluxFlow.Composition` registration helpers for closed generic
JSON schema validator nodes. The package registers explicit
`json.schema-validator` factories, binds `JsonSchemaValidatorOptions` from
composition configuration, compiles inline `schema` or `schemaPath` during
composition build, and resolves optional keyed `IJsonSchemaValueSelector<TInput>`
and `TimeProvider` resources through `FluxFlow.Composition.Hosting`.

## FluxFlow.Engine 2.0.0

Breaking: the expression/mapping abstraction (IFlowExpressionEngine, IFlowMapper, IFlowPredicate,
FlowMapContext, …) is extracted into the new leaf package FluxFlow.Mapping (namespace
FluxFlow.Mapping); the engine now references it. Update usings from `FluxFlow.Engine.Mapping` to
`FluxFlow.Mapping`. In the standalone-node architecture the engine is an optional composition
runtime — components no longer require it.

## FluxFlow.Components.Expressions 2.0.0

Breaking: the shared expression-registration helpers now build on the extracted FluxFlow.Mapping
package instead of FluxFlow.Engine — the package is engine-free. Update usings from
`FluxFlow.Engine.Mapping` to `FluxFlow.Mapping`. Documentation now states that
this is a support package, not a standalone node composition adapter.

## FluxFlow.Mapping 1.0.0

Initial extraction of the expression/mapping abstraction out of `FluxFlow.Engine` into a
standalone leaf package, so nodes can map/filter with host-provided expressions without
referencing the engine.

- Moves `IFlowExpressionEngine`, `IFlowCompiledExpression`, `IFlowMapper`, `IFlowPredicate`,
  `FlowMapContext`, `IFlowMapContextFactory`, the Expression/Delegate mapper+predicate
  adapters, and `EvaluatingCompiledExpression` from `FluxFlow.Engine.Mapping` to the
  `FluxFlow.Mapping` namespace/package. The engine now references this package.

## FluxFlow.Components.Http.AspNetCore 1.0.0

Initial ASP.NET Core HTTP trigger adapter.

- `HttpTriggerNode` is the trigger as a component: given a keyed request source and using
  a `RequestReplyCoordinator` internally, it exposes graph-facing `Output`/`Responses`.
- `AddFluxFlowHttpTrigger(name, configure)` registers the keyed source + trigger + a
  hosted service; `MapFluxFlowTrigger(pattern, name)` feeds it. A
  `MapFluxFlowTrigger(pattern, coordinator)` overload supports DI-less/manual composition.
- `HttpRequestContext` bridges `HttpContext` to the reply contract, writing the correlated
  response (or `504`/`500`/`503` on timeout/failure/shutdown).
- The only FluxFlow package that references ASP.NET Core.

## FluxFlow.Components.RequestReply 1.0.0

Initial request/reply bridge.

- `RequestReplyCoordinator<TRequest, TResponse>` correlates a host-supplied
  `IRequestContext` stream to a one-way graph (`Output` requests, `Responses` input)
  by `CorrelationId`, replies through the context, evicts timed-out requests, and
  reports on broadcast error/event ports. Transport-agnostic — reused by HTTP and
  MQTT triggers.

## FluxFlow.Nodes 1.0.0

Initial shared node kit.

- `FlowNode<TInput, TOutput>` base: a self-contained TPL Dataflow processor with a
  bounded buffered input and broadcast output/error/event ports. Nodes built on it
  run standalone — no engine, registry, or runtime.
- `FlowError` and `FlowEvent` records: the uniform error/event items every node
  emits.

## FluxFlow.Components.Sources 2.0.0

2.0 preview: TimeProvider clock migration.

- Replaces the package's bespoke clock abstraction with System.TimeProvider —
  the clock is now configured via `UseClock(TimeProvider)`/the
  `TimeProvider`-typed `Clock` option, and the old `IXxxClock`/`System*Clock`
  public types are removed (breaking). Runtime behavior is unchanged.

## FluxFlow.Components.Storage.FileSystem 2.0.0

2.0 preview: TimeProvider clock migration.

- Replaces the package's bespoke clock abstraction with System.TimeProvider —
  the clock is now configured via `UseClock(TimeProvider)`/the
  `TimeProvider`-typed `Clock` option, and the old `IXxxClock`/`System*Clock`
  public types are removed (breaking). Runtime behavior is unchanged.

## FluxFlow.Components.Storage.SqlFile 2.0.0

2.0 preview: TimeProvider clock migration.

- Replaces the package's bespoke clock abstraction with System.TimeProvider —
  the clock is now configured via `UseClock(TimeProvider)`/the
  `TimeProvider`-typed `Clock` option, and the old `IXxxClock`/`System*Clock`
  public types are removed (breaking). Runtime behavior is unchanged.

## FluxFlow.Engine 1.3.0

Additive resource accessor (Wave 3 groundwork).

- Adds `RuntimeNodeFactoryContext.GetResource<T>(NodeName)`, a typed accessor
  that resolves a resource node's component-defined handle (e.g. a shared
  connection client) at build time, throwing a clear error if the resource is
  missing or does not provide the requested type. Enables operation nodes to
  reference connection/resource components by name. No existing engine APIs
  change.

## FluxFlow.Components.Control 2.0.0

2.0 preview: compile-once predicates for flow.filter and flow.when.

- `flow.filter` and `flow.when` receive an `IFlowPredicate` compiled once in the
  factory; the node no longer holds the expression engine, the predicate string,
  or the per-message context factory.
- The public node constructors change (breaking on the direct-construction
  path); registration, options, ports, JSON shape, and runtime behavior are
  unchanged.

## FluxFlow.Components.Assertions 2.0.0

2.0 preview: compile-once predicate for flow.assert.

- `flow.assert` receives an `IFlowPredicate` compiled once in the factory;
  assertion result/diagnostic data moves into an injected metadata record; the
  node no longer holds the engine, the expression string, or the context
  factory. The configured clock is still injected.
- Public node constructors change (breaking); registration, options, ports, and
  behavior unchanged.
- Replaces the bespoke `IAssertionClock` abstraction with System.TimeProvider
  (`UseClock` now takes a `TimeProvider`; the old clock interface/implementation
  are removed).

## FluxFlow.Components.Mapping 2.0.0

2.0 preview: compile-once mapper for flow.mapper.

- `flow.mapper` receives an `IFlowMapper` compiled once in the factory instead of
  holding the expression engine and string; it still builds its per-message
  context. The public node constructor changes (breaking); registration,
  options, ports, the Failed port, and behavior unchanged.

## FluxFlow.Components.State 2.0.0

2.0 preview: compile-once reducer + factory relocation for state.reducer.

- The reducer and optional key expression are compiled once in a new
  `StateReducerNodeFactory` and injected via an `IFlowReducer`; the node no
  longer holds the expression engine or references definition/registration
  types.
- The node's static `Create` is removed (relocated to the factory). Registration,
  options, ports, and behavior unchanged.
- Replaces the bespoke `IStateClock` abstraction with `System.TimeProvider`
  (`UseClock` now takes a `TimeProvider`; the old clock interface/implementation
  are removed).

## FluxFlow.Components.Routing 2.0.0

2.0 preview: compile-once selectors for switch, correlation, and join.

- `flow.switch`, `flow.correlation`, and `flow.join` receive their key/route
  selectors as delegates compiled once in the factory; the nodes no longer hold
  the expression engine or per-message context factories.
- Public node constructors change (breaking); registration, options, ports, JSON
  shape, and runtime behavior are unchanged.
- Replaces the bespoke `IRoutingClock` abstraction with System.TimeProvider
  (`UseClock` now takes a `TimeProvider`; the old clock interface/implementation
  are removed).

## FluxFlow.Components.Validation 2.0.0

2.0 preview: schema compiled at build time for the JSON schema validator.

- The JSON schema is now read and compiled once in the factory and injected
  ready; the node performs no file I/O or schema compilation in `StartAsync`, and
  schema-missing/load failures now surface at graph-build time.
- `JsonSchemaValidatorContext` no longer exposes the raw options to value
  selectors (public contract break). Node type, options, ports, and validation
  behavior are unchanged.
- Replaces the bespoke `IValidationClock` abstraction with System.TimeProvider
  (`UseClock` now takes a `TimeProvider`; the old clock interface/implementation
  are removed).

## FluxFlow.Components.Observability 2.0.0

2.0 preview: compile-once predicate for flow.counter (no runtime behavior change).

- `flow.counter` now receives an accept-predicate compiled once in the factory
  (via the engine's build-time `Compile` seam) instead of evaluating the
  predicate expression string on every message. The node no longer holds the
  expression engine or the per-message context factory. Internal refactor;
  node registration, options, ports, and runtime behavior are unchanged.
- Replaces the bespoke `IObservabilityClock` abstraction with System.TimeProvider
  (`UseClock` now takes a `TimeProvider`; the old clock interface/implementation
  are removed).

## FluxFlow.Components.Http 2.0.0

2.0 preview: node factory relocation (no runtime behavior change).

- The node's `static Create(RuntimeNodeFactoryContext …)` factory method is
  moved out of the node type into a dedicated internal `*NodeFactory` class so
  the runtime node no longer references definition/registration types. This
  removes the public `static Create` from the node type (breaking); node
  registration, options, ports, JSON shape, and runtime behavior are unchanged.
- Replaces the bespoke `IHttpClock` abstraction with System.TimeProvider
  (`UseClock` now takes a `TimeProvider`; the old clock interface/implementation
  are removed).
- Introduces a separate `http.client` resource component that owns the client
  configuration (base URL, allowed hosts, redirect policy, timeout, pooling).
  `http.request` now references it by a required `client` name and no longer
  carries client-level config; it resolves the client at build time via the
  engine's `$resources` mechanism. For now the client holds configuration only
  — no `HttpClient` is established — so `http.request` reports a not-connected
  result until a later connect step.
- Adds an explicit, host-driven connect lifecycle: `http.client` now exposes
  `ConnectAsync`/`DisconnectAsync` (plus a `State` and a lock-free
  `TryGetSender`), owning the pooled `HttpClient`/sender built via a new
  client-scoped sender context. `http.request` borrows the sender when connected
  and reports not-connected otherwise; it never connects or disposes. The
  allowed-hosts/redirect SSRF guard is preserved (per-request validation plus
  `AllowAutoRedirect` disabled under a guard). No auto-connect.

## FluxFlow.Components.Metrics 2.0.0

2.0 preview: node factory relocation (no runtime behavior change).

- The node's `static Create(RuntimeNodeFactoryContext …)` factory method is
  moved out of the node type into a dedicated internal `*NodeFactory` class so
  the runtime node no longer references definition/registration types. This
  removes the public `static Create` from the node type (breaking); node
  registration, options, ports, JSON shape, and runtime behavior are unchanged.
- Replaces the bespoke `IMetricsClock` abstraction with System.TimeProvider
  (`UseClock` now takes a `TimeProvider`; the old clock interface/implementation
  are removed).

## FluxFlow.Components.Storage 2.0.0

2.0 preview: node factory relocation (no runtime behavior change).

- The node's `static Create(RuntimeNodeFactoryContext …)` factory method is
  moved out of the node type into a dedicated internal `*NodeFactory` class so
  the runtime node no longer references definition/registration types. This
  removes the public `static Create` from the node type (breaking); node
  registration, options, ports, JSON shape, and runtime behavior are unchanged.
- Replaces the bespoke `IStorageClock` abstraction with System.TimeProvider
  (`UseClock` now takes a `TimeProvider`; the old clock interface/implementation
  are removed).
- Introduces a separate `storage.store` resource component that owns the store
  configuration. `storage.put`/`storage.get`/`storage.query`/`storage.delete`
  now reference it by a required `store` name and no longer open a store
  directly; they resolve it at build time via the engine's `$resources`
  mechanism. For now the store component holds configuration only — no store is
  opened — so the operations report a not-available result until a later open
  step.
- Adds an explicit, host-driven connect lifecycle: `storage.store` now exposes
  `ConnectAsync`/`DisconnectAsync` (plus a `State` and a lock-free
  `TryGetStore`), opening/owning the `IStorageStore` via the configured factory
  (a missing factory reports `StoreOpenFailed` without faulting the runtime).
  `storage.put`/`get`/`query`/`delete` borrow the opened store when connected and
  report not-available otherwise; they never open or dispose it. No auto-connect.

## FluxFlow.Components.Sessions 2.0.0

2.0 preview: node factory relocation (no runtime behavior change).

- The node's `static Create(RuntimeNodeFactoryContext …)` factory method is
  moved out of the node type into a dedicated internal `*NodeFactory` class so
  the runtime node no longer references definition/registration types. This
  removes the public `static Create` from the node type (breaking); node
  registration, options, ports, JSON shape, and runtime behavior are unchanged.
- Replaces the bespoke `ISessionClock` abstraction with System.TimeProvider
  (`UseClock` now takes a `TimeProvider`; the old clock interface/implementation
  are removed).

## FluxFlow.Components.FileSystem 2.0.0

2.0 preview: node factory relocation (no runtime behavior change).

- The node's `static Create(RuntimeNodeFactoryContext …)` factory method is
  moved out of the node type into a dedicated internal `*NodeFactory` class so
  the runtime node no longer references definition/registration types. This
  removes the public `static Create` from the node type (breaking); node
  registration, options, ports, JSON shape, and runtime behavior are unchanged.
- Replaces the bespoke `IFileSystemClock` abstraction with System.TimeProvider
  (`UseClock` now takes a `TimeProvider`; the old clock interface/implementation
  are removed).

## FluxFlow.Components.Timers 2.0.0

2.0 preview: node factory relocation (no runtime behavior change).

- The node's `static Create(RuntimeNodeFactoryContext …)` factory method is
  moved out of the node type into a dedicated internal `*NodeFactory` class so
  the runtime node no longer references definition/registration types. This
  removes the public `static Create` from the node type (breaking); node
  registration, options, ports, JSON shape, and runtime behavior are unchanged.
- Replaces the bespoke `ITimerClock` abstraction with System.TimeProvider
  (`UseClock` now takes a `TimeProvider`; the old clock interface/implementation
  are removed).

## FluxFlow.Components.Mqtt 2.0.0

2.0 preview: node factory relocation (no runtime behavior change).

- The node's `static Create(RuntimeNodeFactoryContext …)` factory method is
  moved out of the node type into a dedicated internal `*NodeFactory` class so
  the runtime node no longer references definition/registration types. This
  removes the public `static Create` from the node type (breaking); node
  registration, options, ports, JSON shape, and runtime behavior are unchanged.
- Replaces the bespoke `IMqttClock` abstraction with System.TimeProvider
  (`UseClock` now takes a `TimeProvider`; the old clock interface/implementation
  are removed).
- Introduces a separate `mqtt.connection` resource component that owns the
  connection profile and reconnect policy. `mqtt.publish`/`mqtt.subscribe` now
  reference it by a required `connectionName` and no longer carry
  `connection`/`reconnect` config; they resolve the connection at build time via
  the engine's `$resources` mechanism. For now the connection holds
  configuration only — no MQTT client is established — so publish/subscribe
  report a not-connected result until a later connect step.
- Adds an explicit, host-driven connect lifecycle: `mqtt.connection` now exposes
  `ConnectAsync`/`DisconnectAsync` (plus a connection `State` and a lock-free
  `TryGetAdapter`), owning the single client lease and health monitor.
  `mqtt.publish`/`mqtt.subscribe` borrow the established adapter when connected
  (subscribe (re)subscribes on connect, deduped per connection epoch) and report
  not-connected otherwise; they never connect or dispose. No auto-connect.

## FluxFlow.Components.Payloads 2.0.0

2.0 preview: node factory relocation (no runtime behavior change).

- The node's `static Create(RuntimeNodeFactoryContext …)` factory method is
  moved out of the node type into a dedicated internal `*NodeFactory` class so
  the runtime node no longer references definition/registration types. This
  removes the public `static Create` from the node type (breaking); node
  registration, options, ports, JSON shape, and runtime behavior are unchanged.

## FluxFlow.Components.Projections 2.0.0

2.0 preview: node factory relocation (no runtime behavior change).

- The node's `static Create(RuntimeNodeFactoryContext …)` factory method is
  moved out of the node type into a dedicated internal `*NodeFactory` class so
  the runtime node no longer references definition/registration types. This
  removes the public `static Create` from the node type (breaking); node
  registration, options, ports, JSON shape, and runtime behavior are unchanged.
- Replaces the bespoke `IProjectionClock` abstraction with System.TimeProvider
  (`UseClock` now takes a `TimeProvider`; the old clock interface/implementation
  are removed).

## FluxFlow.Components.Expectations 2.0.0

2.0 preview: node factory relocation (no runtime behavior change).

- The node's `static Create(RuntimeNodeFactoryContext …)` factory method is
  moved out of the node type into a dedicated internal `*NodeFactory` class so
  the runtime node no longer references definition/registration types. This
  removes the public `static Create` from the node type (breaking); node
  registration, options, ports, JSON shape, and runtime behavior are unchanged.
- Replaces the bespoke `IExpectationClock` abstraction with System.TimeProvider
  (`UseClock` now takes a `TimeProvider`; the old clock interface/implementation
  are removed).

## FluxFlow.Components.Mapping 1.3.0

Failed-output observability release.

- `flow.mapper` now exposes a `Failed` output port so inputs dropped on an
  expression or mapping failure are observable, mirroring `flow.assert`.
- The failed input is emitted on `Failed` in addition to the existing error
  report.

## FluxFlow.Components.Validation 1.3.0

Errors-port release.

- Registers and declares the `Errors` output port on the JSON schema validator
  node so per-message validation errors are wireable and appear in design
  metadata.

## FluxFlow.Components.Sources 1.2.1

Design-metadata correctness and thread-safety release.

- Corrects the source design metadata to match the actual sequence and
  generated-source options (`start`/`step`/`count`, `outputType`, `loop`,
  `maxItems`, timing).
- Makes the type-alias resolution cache fully thread-safe (register writes now
  share the resolve lock).

## FluxFlow.Components.Control 1.2.1

Thread-safety release.

- Makes the type-alias resolution cache fully thread-safe (register writes now
  share the resolve lock).

## FluxFlow.Components.Assertions 1.2.1

Thread-safety release.

- Makes the type-alias resolution cache fully thread-safe (register writes now
  share the resolve lock).

## FluxFlow.Components.Timers 1.2.1

Thread-safety release.

- Makes the type-alias resolution cache fully thread-safe (register writes now
  share the resolve lock).

## FluxFlow.Components.Observability 1.2.1

Thread-safety release.

- Makes the type-alias resolution cache fully thread-safe (register writes now
  share the resolve lock).

## FluxFlow.Engine 1.2.0

Build-time expression compile seam (Wave 1 architecture-review remediation).

- Adds `IFlowExpressionEngine.Compile<T>(string)` returning a reusable
  `IFlowCompiledExpression<T>`. It is default-implemented (defers to `Evaluate`),
  so existing engines keep working unchanged; engines that can pre-parse should
  override it so parsing happens once at build time rather than per message.
- `ExpressionFlowPredicate<TInput>` now compiles its expression once at
  construction and only evaluates the compiled form per message; a new
  `ExpressionFlowMapper<TInput, TOutput>` mirrors this for mapping.
- The engine's own conditional links (`when`) therefore compile once at build
  time instead of re-evaluating the expression string per message.
- Event channels (`EventFlowNodeBase`, the runtime event collector) now use the
  non-lossy fanout source instead of a lossy `BroadcastBlock`, so a slow or late
  event consumer no longer silently misses events.
- `EventFlowNodeBase.EmitEvent` defensively copies event attributes so a caller
  cannot mutate them after emit.

## FluxFlow.Components.Routing 1.2.1

Correctness fixes (Wave 0 architecture-review remediation).

- A per-message key/selector expression failure in the join and window nodes
  now reports a `FlowError` and continues processing instead of rethrowing and
  faulting the whole node, matching the correlation node's behavior.
- Timer cancellation in the correlation, join, and window nodes uses an
  interlocked swap so a cancel can no longer race a dispose of the same token
  source.
- A duplicate correlation side is reported as a warning diagnostic
  (`flow.correlation.duplicateSide`) rather than an error, and the original
  entry's timeout deadline is preserved.

## FluxFlow.Components.Http 1.2.1

Security fix (Wave 0 architecture-review remediation).

- When an `allowedHosts` or `restrictToBaseUrlOrigin` guard is configured,
  automatic redirect following is disabled so a server cannot 3xx-redirect a
  request to a host outside the allow-list (SSRF). Behavior is unchanged when
  no guard is configured.

## FluxFlow.Components.Metrics 1.2.1

Correctness fix (Wave 0 architecture-review remediation).

- Aggregate snapshots are emitted with back-pressure (awaited `SendAsync`)
  instead of being dropped when the bounded output is full, so a slow consumer
  no longer silently loses snapshots.

## FluxFlow.Components.Mqtt 1.2.1

Correctness fix (Wave 0 architecture-review remediation).

- The subscribe node no longer opens a client lease, subscription, or health
  monitor when `Complete()` races `StartAsync` — the completed state is checked
  under the state lock before any resource is acquired.

## FluxFlow.Components.Expectations 1.2.0

Additive observability release.

- Adds an `ObservedEventCount` property on the event expectation node so
  hosts and tests can read the number of recorded events deterministically.
- Makes the expectation timeout test deterministic by waiting for event
  observation before completing the timeout delay; runtime behavior is
  unchanged.

## FluxFlow.Engine 1.1.0

Error-channel and fault-propagation hardening release.

- Node error channels are now broadcast fanout sources so every linked
  consumer receives every error and unobserved errors are discarded instead
  of buffered without bound.
- Unlinked error output ports are drained by the runtime.
- New runtime/workflow/host `Errors` streams (`RuntimeFlowError`, error
  collectors) surface enriched node errors centrally.
- Output ports detach a link whose target no longer accepts messages instead
  of faulting sibling links (new `OutputPort.LinkFailed` event plus
  `flow.link.target.rejected` diagnostic).
- Throwing conditional-link predicates drop the message for that link only
  and report `DynamicExpressionFailed` errors plus
  `flow.link.condition.failed` diagnostics.
- Upstream faults now propagate to linked targets instead of downgrading to
  normal completion.
- Validation rejects node/workflow/resource names containing '.', the
  reserved `$resources` workflow name, and cyclic link paths.
- Node cancellation now completes nodes as stopped instead of faulted.
- `ApplicationRuntime.StartAsync` rejects re-entrant starts.
- Factory registry is thread-safe.
- Configuration scalar coercion only rewrites values that round-trip
  losslessly.

## FluxFlow.Components.Expressions 1.1.0

Context factory resolution hardening release.

- The context factory registry now throws a descriptive error for ambiguous
  incomparable candidate types instead of picking one by dictionary order.

## FluxFlow.Components.Control 1.2.0

Error-port and thread-safety release.

- `flow.filter` and `flow.when` now expose an `Errors` output port (declared
  in design metadata) so flows can observe per-message expression failures.
- Type alias resolution cache is thread-safe.
- Removes dead internal cancellation plumbing.

## FluxFlow.Components.Mapping 1.2.0

Error-port and thread-safety release.

- `flow.mapper` now exposes an `Errors` output port (declared in design
  metadata).
- Type alias resolution cache is thread-safe.
- Removes dead internal cancellation plumbing.

## FluxFlow.Components.Assertions 1.2.0

Deterministic assertion clock release.

- Adds an assertion clock abstraction (`IAssertionClock`, options
  `Clock`/`UseClock`) and stamps `FlowAssertionResult.EvaluatedAt`
  deterministically from it.
- Type alias resolution cache is thread-safe.
- Removes dead internal cancellation plumbing.

## FluxFlow.Components.Timers 1.2.0

Timer correctness and error-port release.

- All timer nodes now expose `Errors` output ports (declared in design
  metadata).
- Cron schedules fire at the first valid instant after a DST spring-forward
  gap instead of skipping the occurrence, and `value/step` cron fields follow
  vixie semantics (value through max).
- `timer.delay` applies a constant arrival+delay offset to bursts instead of
  cumulative serialized delays.
- Dispose is prompt and tolerant of faulted nodes (pending clock delays are
  cancelled).
- Interval/schedule nodes latch completion before start and stop when the
  output declines deliveries.
- Type alias resolution cache is thread-safe.

## FluxFlow.Components.Sources 1.2.0

Source lifecycle hardening release.

- Source nodes latch completion before start, stop when the output declines
  deliveries, and guard cancellation against disposed token sources.
- The generated source constructor validates loop/maxItems invariants.
- Type alias resolution cache is thread-safe.

## FluxFlow.Components.State 1.2.0

Bounded diagnostics release.

- Rejected-key warning tracking is capped with a single summary diagnostic so
  high-cardinality key streams cannot grow memory without bound.
- Removes dead internal cancellation plumbing.

## FluxFlow.Components.Routing 1.2.0

Correlation timeout and design metadata correctness release.

- Correlation timeouts now fire proactively from the package clock without
  requiring subsequent traffic (matching the join node's timer pattern).
- Expiry scanning is arrival-ordered instead of full-map scans per message.
- Duplicate correlation sides preserve the original timeout deadline.
- Join/window timer cancellation is race-free and dispose tolerates faulted
  nodes.
- Design metadata now declares the switch `Matched`/`Routed` ports,
  correlation `Request`/`Response` split inputs, and the correct 30000 ms
  timeout defaults.
- Type alias resolution cache is thread-safe.
- Removes dead internal cancellation plumbing.

## FluxFlow.Components.Http 1.2.0

SSRF and header-injection hardening release.

- Adds `allowedHosts` and `restrictToBaseUrlOrigin` options so hosts can
  restrict per-message request destinations (SSRF hardening; defaults
  preserve existing behavior).
- Rejects header names/values containing CR/LF/NUL before sending.
- Uses a pooled connection lifetime so DNS changes are observed.
- README gains a Security section.

## FluxFlow.Components.FileSystem 1.2.0

Path policy and enumeration hardening release.

- Path policy now treats the working directory as the implicit base when no
  `baseDirectory` is set and absolute paths are disallowed, so relative `..`
  paths can no longer escape (security hardening).
- `file.read` `maxBytes` defaults to 16 MiB when unset (explicit null keeps
  unlimited).
- Directory enumeration no longer blocks startup, skips reparse points when
  recursing, and stops when the output declines.
- `file.watch` exposes `internalBufferSize`, publishes watcher state before
  enabling events, and no longer mislabels shutdown as a full queue.

## FluxFlow.Components.Mqtt 1.2.0

Publish timeout and shutdown hardening release.

- Publishes are bounded by a new `publishTimeoutMilliseconds` option (default
  30000) so a hung adapter can no longer wedge the node.
- Dispose drains pending publishes before releasing the client lease.
- Subscribe handles completion racing startup and stops pumping when the
  output declines.

## FluxFlow.Components.Serialization 1.2.0

Performance maintenance release.

- JSON stringify reuses cached serializer options and parse parses input once
  (performance).
- Removes dead internal cancellation plumbing.

## FluxFlow.Components.Payloads 1.2.0

Bounded payload inspection release.

- Adds a `maxInputBytes` option (default 1 MiB); oversized payloads produce a
  bounded "payload too large" inspection result instead of fully formatting
  arbitrarily large inputs.

## FluxFlow.Components.Validation 1.2.0

Thread-safety maintenance release.

- Type alias resolution cache is thread-safe.
- Removes dead internal cancellation plumbing.

## FluxFlow.Components.Observability 1.2.0

Error-port and metadata accuracy release.

- Counter/logger/metrics nodes now expose `Errors` output ports.
- Design metadata declares accurate per-node option lists instead of one
  shared list.
- Log message rendering is single-pass so substituted values cannot inject
  further placeholders.
- Metrics `AverageSize` divides by sized observations only.
- Type alias resolution cache is thread-safe.
- Removes dead internal cancellation plumbing.

## FluxFlow.Components.Metrics 1.2.0

Bounded tracking and snapshot performance release.

- Rejected-group tracking is capped with a single summary notice.
- Snapshots are built only when emitted (performance) with the final snapshot
  rebuilt at completion.
- Removes dead internal cancellation plumbing.

## FluxFlow.Components.Journal 1.1.0

Retention and lookup performance release.

- The in-memory journal store gains an optional retention-options constructor
  that enforces `MaxRecords` on append.
- Duplicate-id detection is O(1).
- Documentation now states that Journal is host-owned store support, not a
  workflow node composition adapter.

## FluxFlow.Components.Sessions 1.2.0

Design metadata accuracy release.

- Design metadata now matches the actual nodes (query `Output`/`Sessions`
  ports and real recorder/replay/query options, replay `sessionId` required).
- Replay stops when the output declines deliveries instead of counting them
  as emitted.

## FluxFlow.Components.Projections 1.1.0

Design metadata and rate-window correctness release.

- Adds a package-owned `IComponentDesignMetadataProvider` (new dependency on
  `FluxFlow.Components.Designer`).
- Final snapshots compute the rate window against the last matched event
  timestamp so replayed streams report correct rates.

## FluxFlow.Components.Expectations 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` (new dependency on
  `FluxFlow.Components.Designer`).

## FluxFlow.Components.Secrets 1.1.0

Redaction coverage release.

- Default redaction fragments now cover pwd, passphrase, auth, bearer,
  connectionstring, cert, pin, and salt.
- `ShouldRedact` is null-safe.
- Documentation now states that Secrets is a support package consumed by hosts
  and adapters, not a workflow node composition adapter.

## FluxFlow.Components.Resources 1.1.0

Default-instance correctness release.

- `ResourceName.ToString()` returns an empty string for default instances
  instead of null.
- Documentation now states that Resources is a support package consumed by
  hosts and adapters, not a workflow node composition adapter.

## FluxFlow.Components.Storage.FileSystem 1.1.0

Concurrency and query correctness release.

- Store instances are shared per resolved root so optimistic concurrency
  (`ExpectedVersion`, Create mode) is serialized across nodes.
- Queries scan only the resolved collection directory, skip corrupt record
  files, and clean up orphaned temp files.
- Expired records no longer block Create-mode writes.
- Value size limits are measured against compact JSON.

## FluxFlow.Components.Storage.SqlFile 1.1.0

Timestamp and query correctness release.

- Put results now carry the persisted millisecond-precision timestamps so
  re-reads and time-window queries agree.
- Expired records no longer block Create-mode writes.
- Key-prefix and paging filters are pushed into SQL.
- Dispose clears the connection pool so the database file is released.
- A private connection cache replaces the shared cache.

## FluxFlow.Components.Designer 1.0.1

Documentation maintenance release.

- Updates the packaged README guidance for package-owned design metadata
  providers and host catalog composition.
- States that Designer composes metadata, not workflow nodes; engine-aware
  definition identifiers remain part of the metadata contract.
- Keeps Designer public contracts unchanged.

## FluxFlow.Components.Mqtt 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` for public component
  type constants.
- Adds a dependency on `FluxFlow.Components.Designer` for neutral palette,
  editor, validation, and documentation metadata.
- Keeps runtime behavior, node contracts, definitions, JSON shape, and
  registration APIs unchanged.

## FluxFlow.Components.Mapping 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` for public component
  type constants.
- Adds a dependency on `FluxFlow.Components.Designer` for neutral palette,
  editor, validation, and documentation metadata.
- Keeps runtime behavior, node contracts, definitions, JSON shape, and
  registration APIs unchanged.

## FluxFlow.Components.Control 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` for public component
  type constants.
- Adds a dependency on `FluxFlow.Components.Designer` for neutral palette,
  editor, validation, and documentation metadata.
- Keeps runtime behavior, node contracts, definitions, JSON shape, and
  registration APIs unchanged.

## FluxFlow.Components.Assertions 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` for public component
  type constants.
- Adds a dependency on `FluxFlow.Components.Designer` for neutral palette,
  editor, validation, and documentation metadata.
- Keeps runtime behavior, node contracts, definitions, JSON shape, and
  registration APIs unchanged.

## FluxFlow.Components.Sources 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` for public component
  type constants.
- Adds a dependency on `FluxFlow.Components.Designer` for neutral palette,
  editor, validation, and documentation metadata.
- Keeps runtime behavior, node contracts, definitions, JSON shape, and
  registration APIs unchanged.

## FluxFlow.Components.Routing 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` for public component
  type constants.
- Adds a dependency on `FluxFlow.Components.Designer` for neutral palette,
  editor, validation, and documentation metadata.
- Keeps runtime behavior, node contracts, definitions, JSON shape, and
  registration APIs unchanged.

## FluxFlow.Components.Validation 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` for public component
  type constants.
- Adds a dependency on `FluxFlow.Components.Designer` for neutral palette,
  editor, validation, and documentation metadata.
- Keeps runtime behavior, node contracts, definitions, JSON shape, and
  registration APIs unchanged.

## FluxFlow.Components.FileSystem 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` for public component
  type constants.
- Adds a dependency on `FluxFlow.Components.Designer` for neutral palette,
  editor, validation, and documentation metadata.
- Keeps runtime behavior, node contracts, definitions, JSON shape, and
  registration APIs unchanged.

## FluxFlow.Components.Observability 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` for public component
  type constants.
- Adds a dependency on `FluxFlow.Components.Designer` for neutral palette,
  editor, validation, and documentation metadata.
- Keeps runtime behavior, node contracts, definitions, JSON shape, and
  registration APIs unchanged.

## FluxFlow.Components.Timers 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` for public component
  type constants.
- Adds a dependency on `FluxFlow.Components.Designer` for neutral palette,
  editor, validation, and documentation metadata.
- Keeps runtime behavior, node contracts, definitions, JSON shape, and
  registration APIs unchanged.

## FluxFlow.Components.Payloads 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` for public component
  type constants.
- Adds a dependency on `FluxFlow.Components.Designer` for neutral palette,
  editor, validation, and documentation metadata.
- Keeps runtime behavior, node contracts, definitions, JSON shape, and
  registration APIs unchanged.

## FluxFlow.Components.Http 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` for public component
  type constants.
- Adds a dependency on `FluxFlow.Components.Designer` for neutral palette,
  editor, validation, and documentation metadata.
- Keeps runtime behavior, node contracts, definitions, JSON shape, and
  registration APIs unchanged.

## FluxFlow.Components.Serialization 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` for public component
  type constants.
- Adds a dependency on `FluxFlow.Components.Designer` for neutral palette,
  editor, validation, and documentation metadata.
- Keeps runtime behavior, node contracts, definitions, JSON shape, and
  registration APIs unchanged.

## FluxFlow.Components.Metrics 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` for public component
  type constants.
- Adds a dependency on `FluxFlow.Components.Designer` for neutral palette,
  editor, validation, and documentation metadata.
- Keeps runtime behavior, node contracts, definitions, JSON shape, and
  registration APIs unchanged.

## FluxFlow.Components.Sessions 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` for public component
  type constants.
- Adds a dependency on `FluxFlow.Components.Designer` for neutral palette,
  editor, validation, and documentation metadata.
- Keeps runtime behavior, node contracts, definitions, JSON shape, and
  registration APIs unchanged.

## FluxFlow.Components.State 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` for public component
  type constants.
- Adds a dependency on `FluxFlow.Components.Designer` for neutral palette,
  editor, validation, and documentation metadata.
- Keeps runtime behavior, node contracts, definitions, JSON shape, and
  registration APIs unchanged.

## FluxFlow.Components.Storage 1.1.0

Additive design metadata provider release.

- Adds a package-owned `IComponentDesignMetadataProvider` for public component
  type constants.
- Adds a dependency on `FluxFlow.Components.Designer` for neutral palette,
  editor, validation, and documentation metadata.
- Keeps runtime behavior, node contracts, definitions, JSON shape, and
  registration APIs unchanged.

## FluxFlow.Engine 1.0.1

Documentation maintenance release.

- Updates the packaged README and public docs to reflect the stable component
  package `1.0.0` line.
- Records the completed component package stable release track in repository
  memory notes.
- Keeps engine public APIs, definitions, JSON shape, runtime behavior, and
  component contracts unchanged.

## FluxFlow.Components.Resources 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Resources` to `1.0.0`.
- Freezes neutral resource reference, descriptor, lookup result, and diagnostic
  contracts for the stable component line.

## FluxFlow.Components.Secrets 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Secrets` to `1.0.0`.
- Freezes neutral secret reference, descriptor, resolver, redaction, and option
  resolution contracts for the stable component line.

## FluxFlow.Components.Configuration 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Configuration` to `1.0.0`.
- Freezes configuration validation report contracts for resource and secret
  option checks.
- Documentation and package release notes now describe Configuration as a
  support package with host-owned resource and secret ownership, not an engine
  or workflow node adapter.

## FluxFlow.Components.Designer 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Designer` to `1.0.0`.
- Freezes neutral component, option, and port metadata contracts for
  host-generated editors.

## FluxFlow.Components.Expressions 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Expressions` to `1.0.0`.
- Freezes expression engine registration, context factory, and evaluation
  contracts for expression-backed components.

## FluxFlow.Components.Serialization 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Serialization` to `1.0.0`.
- Freezes JSON, text, and base64 parse/format node contracts.

## FluxFlow.Components.Payloads 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Payloads` to `1.0.0`.
- Freezes payload inspection request, result, preview, and classification
  contracts.

## FluxFlow.Components.Validation 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Validation` to `1.0.0`.
- Freezes JSON schema validation options, result, valid, invalid, and error
  routing contracts.

## FluxFlow.Components.Mapping 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Mapping` to `1.0.0`.
- Freezes mapping options, typed mapper node registration, result, error, and
  diagnostic contracts.

## FluxFlow.Components.Control 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Control` to `1.0.0`.
- Freezes filter and when-node expression options, route outputs, errors, and
  diagnostics.

## FluxFlow.Components.Assertions 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Assertions` to `1.0.0`.
- Freezes assertion input, result, passed, failed, error, and diagnostic
  contracts.

## FluxFlow.Components.Sources 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Sources` to `1.0.0`.
- Freezes generated and sequence source options, outputs, completion, and clock
  contracts.

## FluxFlow.Components.Timers 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Timers` to `1.0.0`.
- Freezes interval, schedule, delay, throttle, and debounce timer node
  contracts.

## FluxFlow.Components.Routing 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Routing` to `1.0.0`.
- Freezes switch, correlation, window, join, fork, merge, route envelope, and
  timestamp contracts.

## FluxFlow.Components.Metrics 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Metrics` to `1.0.0`.
- Freezes metric sample, aggregate snapshot, grouping, rate, and clock
  contracts.

## FluxFlow.Components.Observability 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Observability` to `1.0.0`.
- Freezes logger, metrics, counter, structured entry, and diagnostic contracts.

## FluxFlow.Components.Projections 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Projections` to `1.0.0`.
- Freezes event filter, event summary, projection snapshot, and rate contracts.

## FluxFlow.Components.Expectations 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Expectations` to `1.0.0`.
- Freezes event expectation, guard, timeout, result, and diagnostic contracts.

## FluxFlow.Components.Journal 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Journal` to `1.0.0`.
- Freezes journal record, query, query result, retention, and in-memory store
  contracts.

## FluxFlow.Components.State 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.State` to `1.0.0`.
- Freezes reducer input, state result, per-key state, reset, clear, and clock
  contracts.

## FluxFlow.Components.Sessions 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Sessions` to `1.0.0`.
- Freezes session recorder, replay, query, metadata, timing, and store
  abstraction contracts.

## FluxFlow.Components.Storage 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Storage` to `1.0.0`.
- Freezes logical storage request, record, result, query, matcher, delete, and
  clock contracts.

## FluxFlow.Components.Storage.FileSystem 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Storage.FileSystem` to `1.0.0`.
- Freezes the file-backed storage adapter options, query, retention, and
  timestamp behavior.

## FluxFlow.Components.Storage.SqlFile 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Storage.SqlFile` to `1.0.0`.
- Freezes the SQL-file storage adapter options, query, retention, and timestamp
  behavior.

## FluxFlow.Components.FileSystem 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.FileSystem` to `1.0.0`.
- Freezes file write, read, watch, directory enumerate, path policy, and clock
  contracts.

## FluxFlow.Components.Http 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Http` to `1.0.0`.
- Freezes request, response, error, sender abstraction, timeout, and clock
  contracts.

## FluxFlow.Components.Mqtt 1.0.0

Stable component package boundary.

- Promotes `FluxFlow.Components.Mqtt` to `1.0.0`.
- Freezes publish, subscribe, message, result, adapter factory, reconnect hint,
  health event, and clock contracts.

## FluxFlow.Components.Configuration 0.1.0-alpha.1

Configuration validation report package.

- Adds `FluxFlow.Components.Configuration`.
- Adds neutral validation report contracts for resource and secret references.
- Adds `ConfigurationValidator` for resource-only, secret-only, and combined
  validation.
- Preserves ordered diagnostics with source, code, severity, path, name, kind,
  and metadata.

## FluxFlow.Components.Secrets 0.2.0-alpha.1

Secret option reference helpers.

- Adds `SecretOptionReference` for component option models that expose a
  `SecretReference`.
- Adds `SecretOptionResolver` for required, optional, and ordered batch
  resolution through a host-provided resolver.
- Adds `SecretOptionResolution` for option-level resolved, missing, and
  diagnostic outcomes.
- Adds option validation diagnostics with option path metadata.

## FluxFlow.Components.Secrets 0.1.0-alpha.1

Secrets package.

- Adds `FluxFlow.Components.Secrets`.
- Adds neutral secret reference, descriptor, value, and resolver contracts.
- Adds structured diagnostics for missing, duplicate, ambiguous, kind mismatch,
  denied, failed, and invalid secret references.
- Adds redaction helpers for values and sensitive attributes.
- Adds an in-memory resolver for tests and host composition.

## FluxFlow.Components.Storage.SqlFile 0.3.0-alpha.1

Storage query paging support.

- Adds `StorageQueryRequest.Offset` support to the single-file SQL adapter.
- Uses the shared storage query matcher from `FluxFlow.Components.Storage`.

## FluxFlow.Components.Storage.FileSystem 0.3.0-alpha.1

Storage query paging support.

- Adds `StorageQueryRequest.Offset` support to the file-system adapter.
- Uses the shared storage query matcher from `FluxFlow.Components.Storage`.

## FluxFlow.Components.Storage 0.4.0-alpha.1

Storage query paging support.

- Adds `StorageQueryRequest.Offset`.
- Adds `StorageQueryMatcher` for shared query validation and matching.
- Keeps `storage.query` result and record outputs capped by the normalized
  limit.

## FluxFlow.Components.Journal 0.1.0-alpha.1

Journal package.

- Adds `FluxFlow.Components.Journal`.
- Adds `JournalRecord`, `JournalQuery`, and `JournalQueryResult` contracts.
- Adds `IJournalStore` with append, query, and prune operations.
- Adds `JournalQueryMatcher` for reusable event record filtering.
- Adds `InMemoryJournalStore` with duplicate id checks, paging, and retention.
- Adds `FlowEventJournalRecordMapper` for the neutral engine runtime event
  contract.

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
