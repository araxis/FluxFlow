# Progress Log

Date: 2026-05-31

## 2026-06-05 - Component design metadata providers

- Added package-owned `IComponentDesignMetadataProvider` implementations for
  reusable component packages.
- Added `FluxFlow.Components.Designer` project references to the packages that
  expose provider metadata.
- Added coverage tests in `FluxFlow.Components.Designer.Tests` to compose all
  package providers and verify public component type constants have metadata.
- Documented the reusable host-facing model: packages own palette, editor,
  option, port, validation, and documentation metadata; hosts compose providers
  into catalogs and layer host-specific behavior separately.
- Prepared package release metadata for Designer `1.0.1` and affected runtime
  component packages `1.1.0`, keeping the engine at `1.0.1`.
- Verified focused designer metadata tests, release guard tests, full Release
  build, and full Release no-build solution tests.

## Completed

- Inspected `D:\Projects\FluxFlow` and `D:\Projects\FluxMq`.
- Confirmed `FluxFlow` is already a small extracted solution.
- Confirmed `FluxMq` has local changes and was treated as read-only reference.
- Ran the initial test suite successfully.
- Removed transport-specific scenario constants and validation from engine source.
- Removed component event type constants from engine source.
- Changed default configuration section to `FluxFlow:Application`.
- Added package metadata for NuGet packaging.
- Added GitHub CI and NuGet publish workflows.
- Added a GitHub bootstrap helper script.
- Confirmed source, tests, and package README no longer contain source-application transport terms.
- Ran release tests successfully.
- Created local prerelease package files in `artifacts\packages`.
- Initialized git on `main`.
- Created private repository `araxis/FluxFlow`.
- Pushed the initial commit to `origin/main`.
- Updated workflow actions and switched to an Ubuntu runner after the first CI runs reported runner/action notices.
- Stored the NuGet publish credential as repository setting `NUGET_API_KEY`.
- Moved the stale docs set to `memory\legacy-docs`.
- Added a clean docs entrypoint and a documentation consolidation note.
- Added node authoring helpers: base node classes, a runtime node builder, and a registration contract.
- Added focused tests for helper-based source, map, sink, error reporting, and registration.
- Reworked output delivery to reliable runtime fanout without requiring component changes.
- Hardened startup failure cleanup, runtime disposal, build-failure disposal, and node fault diagnostics.
- Added regression tests for fanout delivery, startup failure cleanup, sync/async node disposal, public helper ports, and diagnostic error delivery.
- Closed follow-up review issues: pending fanout sends now cancel on link disposal, raw output source access was removed, startup cleanup preserves the original failure, and fault hooks can publish final diagnostics.
- Closed second runtime review issues: failed-start disposal is best-effort, runtime and workflow disposal now aggregate cleanup errors after trying every owned resource, output ports can be disposed, output pumps cancel cleanly, and build-failure cleanup now releases output ports too.
- Closed final runtime review loop: output pumps now start only after a link or discard drain exists, buffered values are preserved until graph wiring is complete, completion-link cleanup no longer faults inputs during disposal or after cleanup starts, and start cancellation leaves runtime and host state stopped without running fault hooks.
- Closed follow-up review items: helper node fault hooks now run synchronously during fault calls, and runtime/workflow completion continuations preserve faulted state atomically.
- Added a separate diagnostics channel with node helper APIs, runtime/workflow collectors, enriched runtime diagnostic records, focused tests, and public README notes.
- Closed diagnostics review items: diagnostics now use reliable fanout, host diagnostics can be linked before startup, and regression tests cover slow subscribers plus direct receives.
- Recorded the next roadmap: defer FluxMq migration until its current feature work settles, keep a future fluent C# DSL on the roadmap, and plan component families as separate packages.
- Added a release-readiness audit with gates for license metadata, version strategy, dashboard boundary, docs, and release notes.
- Selected MIT licensing, added root `LICENSE`, and added package license metadata.
- Set the default package version to `0.1.0-alpha.1`.
- Removed dashboard/designer metadata from the base engine definition model, validator, and JSON converters.
- Added `CHANGELOG.md` for the first prerelease.
- Upgraded release automation so tag/manual releases build, test, pack, publish NuGet packages, upload artifacts, and create or update GitHub Releases.
- Published `0.1.0-alpha.1` to NuGet and verified package install from the public feed.
- Started `0.2.0-alpha.1` as the engine-only boundary version by removing scenario/test ownership from the core package.
- Started `0.3.0-alpha.1` to rename flow event route metadata to `Channel`.
- Published `0.3.0-alpha.1` and verified a fresh package install from the public feed after clearing stale local HTTP cache.
- Started `0.4.0-alpha.1` to add runtime behavior for link `when` expressions.
- Published `0.4.0-alpha.1` and verified a fresh package install from the public feed.
- Recorded the FluxMq migration result: FluxMq now depends on `FluxFlow.Engine`
  `0.4.0-alpha.1`, keeps its app schema and scenarios outside the engine, and
  still needs FluxMq-side docs cleanup for stale old-pipeline references.
- Recorded the component package roadmap, starting with a future MQTT package
  family after the package-authoring pattern is proven.
- Added a neutral consumer-style sample app that projects app-owned workspace
  metadata into `ApplicationDefinition`, registers typed components explicitly,
  and models bounded Dataflow blocks for sample package authors.
- Added package-authoring registration helpers: `FlowNodeRegistration`,
  `IFlowNodeModule`, and `FlowNodeModule`.
- Started `0.5.0-alpha.1` release prep for package-authoring helpers and the
  neutral consumer sample.
- Published `0.5.0-alpha.1` and verified a fresh public package restore from
  the NuGet feed.
- Rewrote public docs around getting started, definitions, node authoring,
  package authoring, hosting, observability, and workspace projection.
- Added a validation and errors reference page covering definition validation,
  runtime build failures, host lifecycle failures, runtime streams, and
  troubleshooting.
- Added a runtime states reference page covering host state, application runtime
  state, workflow state, startup order, stop/completion behavior, state streams,
  and dashboard usage.
- Added JSON conversion and expression mapping reference pages covering
  serializer options, link JSON forms, workspace projection, condition
  evaluation, custom expression engines, and mapper contracts.
- Added a short package versioning reference page.
- Started the first separate component package template plan around an MQTT
  component package with adapter contracts, module registration, options,
  diagnostics, events, tests, and release workflow impact.
- Added a planning-only component catalog with class-library-per-category
  package shape, planned components by category, a reusable component definition
  template, and development-order options.
- Refined component package planning so reusable packages are designed from
  category-owned contracts, use typed request/options/result records, keep
  `Input` as the default inbound port, and treat the first consumer as boundary
  validation rather than the source of reusable schemas.
- Fixed a Dataflow helper node fault-order race so explicit `Fault(...)` calls
  run node fault hooks before owned blocks can complete through asynchronous
  completion continuations.
- Recorded the component packaging rule: every component family is a separate
  source project in the solution and produces a separate package artifact.
- Added the first MQTT component package project and test project, including
  adapter-backed publish/subscribe nodes, typed request/options/result/message
  contracts, explicit module registration, and release packing for multiple
  source package projects.
- Changed release automation to resolve one package per run from a package
  manifest, keeping solution changes separate from package publication.
- Set the engine project back to its latest engine package version and set the
  MQTT package to its first package-specific prerelease version.
- Started `FluxFlow.Components.Mqtt` `0.2.0-alpha.1` with client factory
  context, explicit adapter ownership, subscription leases, retained
  subscription options, richer diagnostics/events, and split error codes.
- Added Routing `0.6.0-alpha.1` work with `flow.fork`, `flow.merge`, and
  optional switch route envelopes.
- Added Storage `0.2.0-alpha.1` and Storage.FileSystem `0.1.0-alpha.1` work with
  `storage.query`, query contracts, file-system adapter query support, and updated
  storage sample composition.
- Prepared `FluxFlow.Engine` `0.6.0-beta.1` with the public API namespace
  cleanup, host-provided expression boundary, release notes, package metadata,
  sample app update, package pack, and local install smoke test.
- Published `FluxFlow.Engine` `0.6.0-beta.1` and verified a fresh public
  package restore/build smoke test.
- Recorded the first consumer beta migration success and promoted
  `FluxFlow.Engine` to `1.0.0`.
- Published `FluxFlow.Engine` `1.0.0` and verified a fresh public
  package restore/build smoke test.
- Rebuilt and published all current component packages against the stable engine
  boundary to avoid old component binaries referencing the previous
  `FlowNodeId` location.
- Verified a fresh public-feed restore/build smoke with `FluxFlow.Engine`
  `1.0.0` plus all rebuilt component packages.
- Confirmed the first consumer migrated successfully to `FluxFlow.Engine`
  `1.0.0` and the rebuilt component packages.
- Started component maturity work with `FluxFlow.Components.Routing`
  `0.7.0-alpha.1`, adding split `Request` and `Response` inputs for
  `flow.correlation` while preserving the existing single-stream `Input` mode.
- Published `FluxFlow.Components.Routing` `0.7.0-alpha.1` and verified a fresh
  public-feed restore/build smoke test.
- Started shared expression support work with `FluxFlow.Components.Expressions`
  `0.1.0-alpha.1`, adding reusable expression engine and context factory
  registries with focused tests.
- Prepared `FluxFlow.Components.Mapping` `0.2.0-alpha.1` to use the shared
  expression support while preserving the existing Mapping registration API.
- Published `FluxFlow.Components.Expressions` `0.1.0-alpha.1` and verified a
  fresh public-feed restore/build smoke test.
- Published `FluxFlow.Components.Mapping` `0.2.0-alpha.1` and verified a fresh
  public-feed restore/build smoke test.
- Started `FluxFlow.Components.Control` `0.3.0-alpha.1` to use shared
  expression support while preserving the existing Control registration API.
- Published `FluxFlow.Components.Control` `0.3.0-alpha.1` and verified a fresh
  public-feed restore/build smoke test.
- Started `FluxFlow.Components.Assertions` `0.2.0-alpha.1` to use shared
  expression support while preserving the existing Assertions registration API.
- Published `FluxFlow.Components.Assertions` `0.2.0-alpha.1` and verified a
  fresh public-feed restore/build smoke test.
- Started `FluxFlow.Components.State` `0.2.0-alpha.1` to use shared expression
  support while preserving the existing State registration API.
- Published `FluxFlow.Components.State` `0.2.0-alpha.1` and verified a fresh
  public-feed restore/build smoke test.
- Started `FluxFlow.Components.Observability` `0.2.0-alpha.1` to use shared
  expression support while preserving the existing Observability registration
  API.
- Published `FluxFlow.Components.Observability` `0.2.0-alpha.1` and verified a
  fresh public-feed restore/build smoke test.
- Started `FluxFlow.Components.Routing` `0.8.0-alpha.1` to use shared
  expression support while preserving the existing Routing registration API.
- Published `FluxFlow.Components.Routing` `0.8.0-alpha.1` and verified a fresh
  public-feed restore/build smoke test.
- Confirmed shared expression registry storage now lives only in
  `FluxFlow.Components.Expressions`; component packages resolve through the
  shared helper instead of owning local expression registries.
- Started `FluxFlow.Components.Mqtt` `0.3.0-alpha.1` with optional adapter
  health forwarding through diagnostics and events while keeping reconnect
  policy host/adapter-owned.
- Published `FluxFlow.Components.Mqtt` `0.3.0-alpha.1` and verified a fresh
  public-feed restore/build smoke test.
- Started `FluxFlow.Components.Storage.SqlFile` `0.1.0-alpha.1` as a separate
  single-file SQL storage adapter package.
- Published `FluxFlow.Components.Storage.SqlFile` `0.1.0-alpha.1` and verified
  a fresh public-feed restore/build smoke test.
- Started `FluxFlow.Components.Sources` `0.2.0-alpha.1` with host-provided
  source clocks for deterministic delays and sequence timestamps.
- Published `FluxFlow.Components.Sources` `0.2.0-alpha.1` and verified a fresh
  public-feed restore/build smoke test.
- Started `FluxFlow.Components.Sessions` `0.2.0-alpha.1` with host-provided
  session clocks for deterministic recorder timestamps and replay timing.
- Published `FluxFlow.Components.Sessions` `0.2.0-alpha.1` and verified a
  fresh public-feed restore/build smoke test.
- Started `FluxFlow.Components.FileSystem` `0.4.2-alpha.1` to make
  `directory.enumerate.started` deterministic before enumeration work begins.
- Published `FluxFlow.Components.FileSystem` `0.4.2-alpha.1` and verified a
  fresh public-feed restore/build smoke test.
- Started `FluxFlow.Components.Timers` `0.5.0-alpha.1` with host-provided
  timer clocks for deterministic timestamps and delays.
- Published `FluxFlow.Components.Timers` `0.5.0-alpha.1` and verified a fresh
  public-feed restore/build smoke test.
- Started `FluxFlow.Components.Metrics` `0.2.0-alpha.1` with a host-provided
  metrics clock for deterministic fallback sample timestamps.
- Published `FluxFlow.Components.Metrics` `0.2.0-alpha.1` and verified a fresh
  public-feed restore/build smoke test.
- Started `FluxFlow.Components.Routing` `0.9.0-alpha.1` with a host-provided
  routing clock for deterministic route timestamps, windows, joins,
  correlations, and timeout delays.
- Published `FluxFlow.Components.Routing` `0.9.0-alpha.1` and verified a fresh
  public-feed restore/build smoke test.
- Started `FluxFlow.Components.Observability` `0.3.0-alpha.1` with a
  host-provided observability clock for deterministic logger, counter, and
  metrics timestamps.
- Published `FluxFlow.Components.Observability` `0.3.0-alpha.1` and verified a
  fresh public-feed restore/build smoke test.
- Started `FluxFlow.Components.State` `0.3.0-alpha.1` with a host-provided
  state clock for deterministic reducer result timestamps.
- Published `FluxFlow.Components.State` `0.3.0-alpha.1` and verified a fresh
  public-feed restore/build smoke test.
- Started `FluxFlow.Components.Http` `0.2.0-alpha.1` with a host-provided HTTP
  clock for deterministic response and error timestamps.
- Published `FluxFlow.Components.Http` `0.2.0-alpha.1` and verified a fresh
  public-feed restore/build smoke test.
- Started `FluxFlow.Components.FileSystem` `0.5.0-alpha.1` with a
  host-provided file system clock for deterministic write, read, watch, and
  enumerate timestamps.
- Published `FluxFlow.Components.FileSystem` `0.5.0-alpha.1` and verified a
  fresh public-feed restore/build smoke test.
- Started `FluxFlow.Components.Validation` `0.2.0-alpha.1` with a
  host-provided validation clock for deterministic JSON schema validation
  result timestamps.
- Published `FluxFlow.Components.Validation` `0.2.0-alpha.1` and verified a
  fresh public-feed restore/build smoke test.
- Started coordinated storage clock hardening for
  `FluxFlow.Components.Storage` `0.3.0-alpha.1`,
  `FluxFlow.Components.Storage.FileSystem` `0.2.0-alpha.1`, and
  `FluxFlow.Components.Storage.SqlFile` `0.2.0-alpha.1`.
- Published `FluxFlow.Components.Storage` `0.3.0-alpha.1`,
  `FluxFlow.Components.Storage.FileSystem` `0.2.0-alpha.1`, and
  `FluxFlow.Components.Storage.SqlFile` `0.2.0-alpha.1`; verified a fresh
  public-feed restore/build smoke test.
- Started `FluxFlow.Components.Mqtt` `0.4.0-alpha.1` with a host-provided MQTT
  clock for deterministic publish result and package-owned workflow event
  timestamps.
- Published `FluxFlow.Components.Mqtt` `0.4.0-alpha.1` and verified a fresh
  public-feed restore/build smoke test.
- Started `FluxFlow.Components.Routing` `0.10.0-alpha.1` with explicit result
  timestamps so Routing contracts no longer create hidden current times.
- Published `FluxFlow.Components.Routing` `0.10.0-alpha.1` and verified a fresh
  public-feed restore/build smoke test.
- Started `FluxFlow.Components.Sessions` `0.3.0-alpha.1` with a neutral
  `session.query` node for session metadata queries.
- Published `FluxFlow.Components.Sessions` `0.3.0-alpha.1` and verified a fresh
  public-feed restore/build smoke test.
- Started `FluxFlow.Components.Mqtt` `0.5.0-alpha.1` with adapter-owned
  reconnect policy hints on the MQTT client factory context.
- Published `FluxFlow.Components.Mqtt` `0.5.0-alpha.1` and verified a fresh
  public-feed restore/build smoke test.
- Started `FluxFlow.Components.Projections` `0.1.0-alpha.1` with a neutral
  `event.projection` node, event filter contracts, latest-event summaries,
  rolling-rate snapshots, deterministic projection clocks, and focused tests.
- Published `FluxFlow.Components.Projections` `0.1.0-alpha.1` and verified a
  fresh public-feed restore/run smoke test.
- Started `FluxFlow.Components.Expectations` `0.1.0-alpha.1` with neutral
  `event.expect` and `event.guard` nodes, expectation result contracts,
  deterministic timeout clocks, and focused tests.
- Published `FluxFlow.Components.Expectations` `0.1.0-alpha.1` and verified a
  fresh public-feed restore/run smoke test.
- Started `FluxFlow.Components.Designer` `0.1.0-alpha.1` with neutral
  component, option, and port metadata contracts plus catalog/provider helpers.
- Published `FluxFlow.Components.Designer` `0.1.0-alpha.1` and verified a
  fresh public-feed restore/run smoke test.
- Started `FluxFlow.Components.Resources` `0.1.0-alpha.1` with neutral named
  resource references, descriptors, lookup results, diagnostics, and catalog
  helpers.
- Published `FluxFlow.Components.Resources` `0.1.0-alpha.1` and verified a
  fresh public-feed restore/run smoke test.
- Started `FluxFlow.Components.Journal` `0.1.0-alpha.1` with neutral journal
  records, query filters, store abstraction, retention options, an in-memory
  store, and focused tests.
- Published `FluxFlow.Components.Journal` `0.1.0-alpha.1` and verified a fresh
  public-feed restore/run smoke test.
- Started storage query paging hardening with shared query validation and
  `Offset` support across core storage contracts plus file-backed and
  single-file SQL-backed adapters.
- Published `FluxFlow.Components.Storage` `0.4.0-alpha.1`,
  `FluxFlow.Components.Storage.FileSystem` `0.3.0-alpha.1`, and
  `FluxFlow.Components.Storage.SqlFile` `0.3.0-alpha.1`; verified a fresh
  public-feed restore/run smoke test.
- Started `FluxFlow.Components.Secrets` `0.1.0-alpha.1` with neutral secret
  references, descriptors, resolver contracts, redaction helpers, structured
  diagnostics, and an in-memory resolver for tests and host composition.
- Published `FluxFlow.Components.Secrets` `0.1.0-alpha.1` and verified a fresh
  public-feed restore/run smoke test.
- Started `FluxFlow.Components.Secrets` `0.2.0-alpha.1` with option-facing
  secret reference helpers so component options can hold `SecretReference`
  while hosts own resolution.
- Published `FluxFlow.Components.Secrets` `0.2.0-alpha.1` and verified a fresh
  public-feed restore/run smoke test.
- Started `FluxFlow.Components.Configuration` `0.1.0-alpha.1` with a combined
  validation report for resource references and secret-backed option references.
- Published `FluxFlow.Components.Configuration` `0.1.0-alpha.1` and verified a
  fresh public-feed restore/run smoke test.
- Added a release-audit test project that verifies package manifest entries
  match project metadata, packed readmes, and changelog headings.
- Extended release-audit tests to cover release resolver and release-notes helper
  scripts directly.
- Extended release-audit tests to ensure all source package projects are listed
  in the package manifest and helper scripts reject invalid inputs.
- Added release-audit package convention checks for target frameworks, package
  metadata, symbol settings, and manifested project references.
- Added a package consumer smoke harness and wired release flow to restore,
  build, run, and load package types from the packed artifact before publishing.
- Added a package archive inspection harness and wired release flow to validate
  packed archive contents before consumer smoke.
- Added a post-publish package feed verification harness and wired release flow
  to restore, build, run, and load the exact published package version from an
  isolated consumer cache.
- Added a local package release dry-run harness that resolves one package, packs
  it, inspects archives, runs local consumer smoke, and runs local feed-style
  verification.
- Added a guarded release tag helper that resolves one package, requires a clean
  working tree, runs the local dry run, and creates the release tag only after
  the dry run passes.
- Added a package release operator note with the local dry-run and guarded tag
  commands.
- Added a read-only package listing helper that prints package aliases, current
  versions, release tags, package ids, and project paths.
- Added a read-only release preflight helper that resolves one package and
  prints exact dry-run and guarded tag commands with the current version.
- Extended release preflight to verify the selected package changelog section
  before printing guarded tag commands.
- Started component package `1.0.0` readiness with a wave-based readiness
  matrix, package version bumps, and changelog entries for all components.
- Fixed package archive inspection to accept the schema emitted by the pack
  tool and extended local feed-style verification to allow an extra dependency
  source when a local dry run verifies packages with shared dependencies.
- Completed component package `1.0.0` local gates across all four waves:
  full Release build, full no-build test suite, release preflight for every
  component, and package dry runs from the local v1 package source.
- Committed and pushed the component stable-release preparation to `main`.
- Created stable component release tags in dependency wave order.
- Verified all 28 package aliases are at `1.0.0`.
- Confirmed all 28 stable release tags exist locally and on the remote.
- Confirmed all 28 package release records exist.
- Confirmed all 28 package `1.0.0` versions are visible on the public package
  feed.
- Re-ran the full Release build successfully with no warnings or errors.
- Re-ran the full Release no-build test suite successfully across 30 test
  assemblies and 595 tests.
- Added package-owned design metadata providers for reusable component
  packages so host catalog adapters can compose package descriptors directly
  while keeping host-only overrides outside reusable package metadata.
- Completed a read-only full-solution code review (engine, all component
  packages, release tooling) and recorded findings, test gaps, and
  remediation priorities in `131-full-code-review.md`.
- Fixed all review findings: engine error channels became broadcast fanout
  sources with runtime/workflow/host error streams, link-failure isolation,
  fault propagation, and stricter validation; registered missing Errors
  ports; hardened Http/FileSystem/Mqtt/storage security and concurrency;
  corrected designer metadata; fixed release tooling injection and gates.
  Engine moved to 1.1.0 and 24 component packages to new minors with
  changelog sections; full Release suite green at 684 tests across 30
  assemblies (`132-review-remediation-release.md`).
- Released the remediation wave: 25 guarded tags pushed in dependency-wave
  order, all 25 publish workflow runs succeeded, and all new versions are
  visible on the public package feed.
- Made the expectation timeout test deterministic with an additive
  `ObservedEventCount` node property and released
  `FluxFlow.Components.Expectations` `1.2.0`
  (`133-expectations-deterministic-timeout-test.md`).
- Added a flat-container index pre-check to `package-feed-verify.ps1` so the
  post-publish verification step absorbs nuget.org indexing lag instead of
  burning restore attempts (`134-feed-verify-index-precheck.md`).
- Ran a deep per-component architecture review against four owner principles
  and recorded the issue list + Wave 0-3 roadmap to 2.0
  (`135-architecture-review-and-roadmap.md`).
- Implemented Wave 0 correctness fixes (Routing join/window rethrow + timer
  CTS race + correlation duplicate-side warning; HTTP redirect SSRF guard;
  Metrics snapshot back-pressure; MQTT subscribe completed-before-start) with
  regression tests; bumped Routing/Http/Metrics/Mqtt to 1.2.1; full Release
  suite green at 691 tests across 30 assemblies.
- Started Wave 1: added the build-time expression compile seam
  (`IFlowExpressionEngine.Compile<T>` default-implemented +
  `IFlowCompiledExpression<T>`; `ExpressionFlowPredicate`/new
  `ExpressionFlowMapper` compile once); engine bumped to 1.2.0; full Release
  suite green at 694 tests. (Wave 0 fixes remain merged-but-unpublished per
  owner decision to batch the release.)
- Completed the rest of Wave 1: engine event channels now use the non-lossy
  fanout source (EventFlowNodeBase + FlowEventCollector) with defensive event
  attribute copies; flow.mapper gained a Failed output port (Mapping 1.3.0);
  Validation declared/wired its Errors port (1.3.0); the type-alias resolution
  cache is thread-safe and Sources design metadata corrected (Control/
  Assertions/Timers/Sources/Observability 1.2.1). Deferred with rationale:
  fanout-pump consolidation (#9, maintainability-only, real concurrency risk),
  FlowNodeBase pump disposal (#12, unsafe because RuntimeNodeDisposal dispatches
  IDisposable before IAsyncDisposable), and converting package event sources to
  wireable non-lossy ports (needs public FlowFanoutSource + the events-as-ports
  decision — Wave 2). Full Release suite green at 695 tests across 30 assemblies.
- Scoped the breaking 2.0 work as a review-ready plan (`136-wave2-2.0-plan.md`):
  per-node compile-once transformation, JsonSchemaValidator fix, factory
  relocation worklist, breaking-surface summary, and sequencing.
- Published the Waves 0+1 batch to NuGet (engine `1.2.0` first, then 11
  components: Mapping/Validation `1.3.0`, Routing/Http/Metrics/Mqtt/Sources/
  Control/Assertions/Timers/Observability `1.2.1`). All 12 publish runs
  succeeded first try (flat-container pre-check absorbed indexing lag); all 12
  versions verified live on the public feed.
- Started Wave 2 (2.0 track, held unpublished): step 1 relocated the co-located
  `static Create(RuntimeNodeFactoryContext …)` out of node types into dedicated
  `*NodeFactory` classes for Http, Metrics, Storage, Sessions, FileSystem,
  Timers (Interval/Schedule), Mqtt, Payloads, Projections, Expectations — pure
  refactor, zero behavior change, full suite green at 695 tests. Those 10
  packages bumped to `2.0.0-preview.1` (removing the public `static Create` is
  breaking). Engine and the expression/State/Validation packages untouched in
  this step.
- Wave 2 step 2: flow.counter compiles its accept-predicate once in the factory
  (Observability `2.0.0-preview.1`) — proved the compile-once pattern.
- Wave 2 steps 3-8: converted the remaining expression nodes to factory-compiled
  delegates — flow.filter/flow.when (Control), flow.assert (Assertions),
  flow.mapper (Mapping), state.reducer (State, via a new IFlowReducer + factory
  relocation), flow.switch/correlation/join (Routing); and fixed the
  JsonSchemaValidator config leak (schema read+compiled in the factory, no node
  file I/O, options no longer leaked to selectors). Nodes now hold only typed
  delegates + a precomputed engine-name string; public node ctors changed
  (2.0 breaks on the direct-construction path). Control/Assertions/Mapping/
  State/Routing/Validation bumped to `2.0.0-preview.1`. Full Release suite green
  at 695 tests across 30 assemblies. Wave 2 implementation complete; the whole
  2.0 set stays unpublished (preview) until release is approved.

- Wave 3 step A: added the additive `RuntimeNodeFactoryContext.GetResource<T>`
  engine accessor (engine `1.3.0`, merged) — the build-time resolution
  primitive for connection-resource components.
- Wave 3 TimeProvider consolidation: replaced all 15 bespoke `IXxxClock`
  interfaces (+ their `System*`/`Recording*` doubles) with `System.TimeProvider`
  across every clock-bearing component package; standardized the option API on
  `UseClock(TimeProvider)`/`Clock`; replaced test doubles with
  `Microsoft.Extensions.TimeProvider.Testing` `FakeTimeProvider` (10.7.0) plus
  bespoke throwing `TimeProvider`s for fault-injection tests. Hardened the
  FakeTimeProvider timeout tests to gate `Advance` on timer registration and
  removed real-time/synchronous-assertion races (Routing, Validation) — full
  Release suite verified stable across 19 consecutive solution-wide runs.
  Sources/Storage.FileSystem/Storage.SqlFile bumped to `2.0.0-preview.1`; the
  other clock packages already on the 2.0 track gained a clock changelog
  bullet. Engine has no clock and is unaffected. Connection-resource components
  (mqtt.connection/http.client/storage.store) remain the last Wave 3 step.
- Wave 3 connection components (MQTT template): added a separate `mqtt.connection`
  resource component (`IMqttConnectionHandle`, `MqttConnectionNode`/options/
  factory, `mqtt.connection` type) that owns the connection profile + reconnect
  policy. `mqtt.publish`/`mqtt.subscribe` now reference it by required
  `connectionName`, resolve it at build via `GetResource<IMqttConnectionHandle>`,
  and no longer carry connection/reconnect config or create/connect/dispose any
  client. Per the owner's explicit choice, this step is CONFIG-ONLY: no client is
  established, so publish/subscribe report a not-connected result until a later
  connect step (deliberate intermediate state; round-trip/health/lease tests were
  removed/rewritten). Mqtt stays `2.0.0-preview.1`. Full suite green at 692 tests.
  HTTP/Storage connection components still pending.
- Wave 3 connection components (HTTP + Storage, mirroring MQTT, config-only):
  added `http.client` (owns base URL/allowed hosts/redirects/timeout/pooling;
  http.request references it by required `client`, reports RequestNotConnected,
  holds no HttpClient) and `storage.store` (owns store config; storage.put/get/
  query/delete reference it by required `store`, report StoreNotAvailable, open
  no store). Both resolve via `GetResource<T>` at build. Removed the now-obsolete
  through-node round-trip integration tests from the FileSystem/SqlFile adapter
  test projects (direct-adapter coverage retained); added the two new types to
  the Designer coverage test. Http/Storage stay `2.0.0-preview.1`. Full suite
  green at 679 tests. Wave 3 connection-component separation is complete for all
  three protocols (all config-only; the shared-client/open "connect step" is the
  remaining future work, deferred by owner choice). The full 2.0 set (Waves 2+3)
  is implemented and unpublished on the preview track.

- Wave 3 connect step (host-API only, explicit, no auto-connect — owner choice):
  made the three connection components functional. Each handle gained
  `ConnectAsync`/`DisconnectAsync` + a connection `State` + a lock-free
  `TryGet*` borrow accessor over a single-flight gated core (set client first /
  state Connected last; clear/null first on disconnect; resources dispose last
  = authoritative teardown). mqtt.connection owns the single lease + health
  monitor; publish/subscribe borrow the adapter (subscribe (re)subscribes on
  connect, deduped per connection epoch). http.client owns the pooled sender
  (built via a new client-scoped sender context; SSRF allow-list/redirect guard
  preserved). storage.store opens/owns the store via the factory (missing
  factory → StoreOpenFailed, never faults the runtime). Operations borrow when
  connected, report not-connected/not-available otherwise, and never connect or
  dispose. An in-graph command-port trigger was ruled out (resource nodes can't
  be link targets without an engine change). Fixed FakeTimeProvider test
  flakiness uncovered under heavy parallel load: a capture-after-count
  lost-wakeup in the Sessions/Sources advance helpers (capture the registration
  signal before the count check) and over-aggressive 5s positive-wait timeouts
  in the Routing tests (raised to 30s); full Release suite stable across 12
  consecutive solution-wide runs, 705 tests. Mqtt/Http/Storage stay
  `2.0.0-preview.1`. This makes the full 2.0 set (Waves 2+3) functional and
  publishable; it remains unpublished on the preview track pending the publish
  decision.

- 2.0 GA remediation + cut (owner decision "Blocker + all confirmed fixes, then
  cut GA"). Pre-release review returned NO-GO on one blocker + confirmed fixes.
  Blocker: `FluxFlow.Components.State` still shipped a bespoke `IStateClock`
  (missed by the TimeProvider sweep; an earlier commit falsely claimed it
  migrated) — migrated to `System.TimeProvider`. Confirmed fixes: connection-node
  dispose-race lease leak in all three nodes (decide-and-publish guard + gate-
  disposed tolerance, plus connect-fault/disconnect-wins/dispose-races tests); a
  `BespokeClockInterfaceTests` release guard asserting no `src` package re-adds an
  `IXxxClock` (would have caught the State miss); restored the descriptive
  `MapperFailed` diagnostic in Mapping; refreshed the Mqtt/Http/Storage/Timers
  packaged READMEs to the 2.0 shapes. Fixed three load-only flakes at root cause:
  FlowWindow real-clock duration coupled to the ~15.6ms Windows tick (assert
  positive elapsed; exact value pinned with a fake clock in RoutingClockTests),
  FlowJoin one-shot clock-fault landing on a non-deterministic message (send the
  failing message alone and await its error first), and StorageStore reading
  secondary fanout ports (Found/Records/Diagnostics) before the pump delivered
  (await the item); standardized positive waits to 30s in Sessions/Sources/
  Timers/Expectations. Stability: Routing 50/50, Storage 40/40, full solution
  15/15 green at 717 tests. Cut GA: flipped the 20 component packages
  `2.0.0-preview.1` -> `2.0.0` (csproj + CHANGELOG headings; preflight/get-release-
  notes key off the heading). Engine ships `1.3.0` (additive); publish engine
  first (ProjectReference bakes a `>= 1.3.0` floor), then the 20 `2.0.0`
  components, then verify the feed. PUBLISHED + verified: all 21 GA packages
  (engine `1.3.0` + 20 components `2.0.0`) are live and indexed on nuget.org;
  21 git tags + 21 GitHub releases exist. Note: a single `git push` of all 17
  tier-1 tags triggered no workflow runs (GitHub suppresses push events for
  >3 tags pushed at once); re-triggered via `workflow_dispatch` per package
  (resolves version from csproj, reuses the existing tag). For future
  multi-package releases, push tags in batches of <=3 or dispatch.

- Standalone-node re-architecture (branch `work/http-simplify`, in progress,
  unmerged, unpublished — full detail in [[139-standalone-node-architecture]]).
  Owner principle: build nodes that connect, not a framework; a node must run
  with no engine (`new` it, post input, `LinkTo` output), delegating complexity
  to TPL Dataflow and the real library (HttpClient, the host's server, an MQTT
  client). Three layers: the `FluxFlow.Nodes` kit (`FlowNode<TIn,TOut>` with a
  bounded BufferBlock input + BroadcastBlock output/error/event ports; the
  `FlowMessage<T>` envelope carrying a guarded strong `CorrelationId` that flows
  via `With`; FlowError/FlowEvent stamped with it) -> self-contained component
  nodes -> optional composition/host. Reworked so far: `FluxFlow.Components.Http`
  collapsed to one engine-free `HttpClientNode` over an injected HttpClient (SSRF
  guard, pooling, redirects all move to the injected client / a DelegatingHandler);
  new `FluxFlow.Components.RequestReply` (`RequestReplyCoordinator<TReq,TResp>` brings
  request/reply to the one-way graph, correlating on CorrelationId, with timeout
  eviction and reliable bounded request delivery); new
  `FluxFlow.Components.Http.AspNetCore` (the only ASP.NET-aware package;
  `MapFluxFlowTrigger`); new `FluxFlow.Components.Mqtt.RequestReply` (same bridge,
  no MQTT-library dep — transport-neutrality proof). Full solution green at 731
  tests, 0 warnings; real-server end-to-end via ASP.NET Core TestServer. The other
  ~27 components remain engine-coupled; rolling the kit/envelope across them is the
  large migration gated on an explicit go-ahead.

## Remaining

- 2.0 GA line is fully published. (The `1.0.0` component release track is also
  complete.)
- Standalone-node re-architecture: HTTP + request/reply + HTTP/MQTT triggers done
  on `work/http-simplify`; pending decisions — roll the kit/envelope across the
  remaining ~27 components, then version/publish the reworked packages (new major
  line). See [[139-standalone-node-architecture]].
