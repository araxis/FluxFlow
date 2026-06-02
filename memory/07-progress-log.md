# Progress Log

Date: 2026-05-31

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

## Remaining

- Continue the broader component maturity backlog.
