# Progress Log

Date: 2026-05-31

## 2026-07-19 - vNext Projection Results

- Added `FlowEventProjectionNode` with typed ProjectionEvent input and one
  `FlowResult<EventProjectionSnapshot>` Output plus Events.
- Preserved ordered counts, filters, previews, replay-time rolling rates, fan-out,
  and strong message lineage; configured final snapshots now emit exactly once
  after normal completion drains accepted input.
- Made expected projection failures normal result data with stable string codes
  while preserving later-event continuation.
- Migrated Projections Composition and Designer metadata to the canonical fixed
  Output and no universal Errors surface while retaining the direct-result node
  for compatibility.
- Projections moved to `4.0.0` and Projections Composition to `2.0.0`; package
  docs, shared docs, changelog, and source-declaration baseline were updated.
- Runtime (`17`), Composition (`12`), core Composition (`126`), Designer (`98`),
  Hosting (`38`), Release (`93`), and full Release (`2,064`) tests passed.
  Controlled Debug/Release builds, binary compatibility, release preflight,
  package dry-runs, and a package-only consumer also passed.
- Metrics is the next separately bounded component-family assessment.

## 2026-07-19 - vNext FlowValue State

- Added `FlowValueStateReducerNode` with typed commands, immutable FlowValue
  state, one normal `FlowResult<FlowValueStateReducerResult>` Output, and Events.
- Added updated/reset/cleared success variants plus normal invalid-message,
  key, expression, reducer, and key-limit failures with stable string codes.
- Migrated State Composition and Designer metadata to canonical fixed ports,
  no universal Errors surface, exact host-owned resources, and natural JSON
  `initialState` decoding.
- Preserved the complete object-based standalone State node as an explicit
  compatibility surface.
- State moved to `4.0.0` and State Composition to `2.0.0`; package docs, shared
  docs, changelog, and source-declaration baseline were updated.
- Runtime (`28`), Composition (`15`), core Composition (`126`), Designer (`98`),
  Hosting (`38`), Release (`93`), and full Release (`2,057`) tests passed.
  Controlled Debug/Release builds, binary compatibility, release preflight,
  package dry-runs, and a package-only consumer also passed.
- Projections is the next separately bounded component-family assessment.

## 2026-07-19 - vNext Control Link Deprecation

- Made canonical conditioned links the filtering and branching primitive; no
  redundant FlowValue Control node was added.
- Marked `FilterNode<TInput>`, `WhenNode<TInput>`, and their Composition
  registrations obsolete while preserving released behavior and contracts.
- Marked both Designer entries deprecated with canonical-link migration
  guidance while retaining legacy options, ports, aliases, and resources.
- Control moved to `4.0.0` and Control Composition to `2.0.0`; package docs,
  shared docs, and changelog now show flat conditioned-link replacements.
- Runtime (`30`), Composition (`19`), core Composition (`126`), Designer (`98`),
  Hosting (`38`), Release (`93`), and full Release (`2,051`) tests passed.
  Controlled Debug/Release builds, binary compatibility, release preflight,
  package dry-runs, and a package-only consumer also passed.
- State is the next separately bounded component-family assessment.

## 2026-07-19 - vNext FlowValue Routing

- Added canonical FlowValue Window, Correlation, and Join nodes with one normal
  `FlowResult<T>` Output plus Events. Windows, matches, and timeouts are success
  variants; expected selector, validation, and capacity failures are normal
  error variants.
- Preserved message lineage across success, timeout, and operation failures and
  hardened adapter completion/fault handling.
- Added parameterless canonical Composition registrations and FlowValue/result
  Designer ports while retaining explicit generic compatibility registrations.
- Marked Switch, Fork, and Merge runtime nodes, registrations, and metadata
  obsolete/deprecated because canonical links own condition, fan-out, and
  shared-input fan-in semantics.
- Routing moved to `4.0.0` and Routing Composition to `2.0.0`; declaration
  baselines, package docs, shared docs, and changelog were updated.
- Runtime (`86`), Composition (`19`), core Composition (`126`), Designer (`98`),
  Hosting (`38`), Release (`93`), and full Release (`2,048`) tests passed.
  Controlled Debug/Release builds, binary compatibility, release preflight,
  package dry-runs, and a package-only consumer also passed.
- Control is the next separately bounded component-family assessment.

## 2026-07-19 - vNext Expectation Results

- Added canonical `FlowEventExpectationNode` with one normal
  `FlowResult<EventExpectationResult>` Output and Events. Matched, unmet,
  timeout, and ordered completion are successful variants; expected evaluation
  failure is one normal error result.
- Added exact-once trigger arbitration, ordered completion after accepted input,
  deterministic clocks, retained projection-event evidence, stable result/error
  strings, diagnostics, and strong message lineage.
- Migrated Expectations Composition registration and Designer metadata to the
  canonical fixed output and no universal Errors surface while preserving the
  released standalone node in the runtime package.
- Moved Expectations to `4.0.0` and Expectations Composition to `2.0.0`,
  updated package docs/changelog/API baseline, and documented the explicit
  typed-result boundary.
- Passed 2,041 Release tests across 63 projects, controlled zero-warning
  Debug/Release builds across 130 projects, binary compatibility, two release
  preflights/dry-runs, and a package-only net8 consumer. Full evidence is in
  [[221-vnext-expectations-flowresult]].
- Routing is the next separately bounded component-family assessment.

## 2026-07-19 - vNext FlowValue Assertions

- Added canonical `FlowValueAssertionNode` with one normal
  `FlowResult<FlowValueAssertionResult>` output and Events. Passed and failed
  rules are successful variants; missing input and expression evaluation
  failures remain normal workflow data.
- Added canonical options, transport-neutral result data, stable result/error
  strings, exact value preservation, message lineage, compile-once predicate
  evaluation, FlowValue context support, and later-message continuation.
- Migrated parameterless Assertions Composition registration and Designer
  metadata to canonical fixed ports and `Resources.{name}` engine/context/clock
  addresses. Existing generic registration retains its branch/error surfaces.
- Moved Assertions to `4.0.0` and Assertions Composition to `2.0.0`, updated
  package docs/changelog/API baseline, and documented the explicit typed-result
  boundary.
- Passed 2,030 Release tests across 63 projects, controlled zero-warning
  Debug/Release builds across 130 projects, binary compatibility, two release
  preflights/dry-runs, and a package-only net8 consumer. Full evidence is in
  [[220-vnext-assertions-flowvalue]].
- Expectations is the next separately bounded component-family migration.

## 2026-07-19 - vNext FlowValue Validation

- Added canonical `FlowValueJsonSchemaValidatorNode` with one normal
  `FlowResult<JsonSchemaFlowValueValidationResult>` output and Events. Valid and
  invalid evaluations are successful variants; expected selector and schema
  failures remain normal workflow data.
- Added the transport-neutral `IJsonSchemaFlowValueSelector` and deterministic
  ordinary-JSON conversion while preserving exact values, validation issues,
  schema metadata, and message lineage.
- Migrated parameterless Validation Composition registration and Designer
  metadata to the canonical fixed ports and `Resources.{name}` selector/clock
  addresses. The existing generic node and registration remain available for
  code-authored compatibility.
- Moved Validation to `4.0.0` and Validation Composition to `2.0.0`, updated
  package docs/changelog/API baseline, and documented that typed results are not
  implicitly unwrapped on links.
- Passed 2,021 Release tests across 63 projects, controlled zero-warning
  Debug/Release builds, binary compatibility, two release preflights/dry-runs,
  and a package-only net8 consumer. Full evidence is in
  [[219-vnext-validation-flowvalue]].
- Assertions followed as the next bounded component-family migration.

## 2026-07-18 - vNext FlowContent And FlowValue Serialization

- Added six canonical standalone nodes for explicit JSON, text, and Base64
  conversion between `FlowContent` and `FlowValue`, with one normal
  `FlowResult<T>` output and no universal Error port.
- Kept expected format, type, size, null-input, and encoding failures as stable
  normal result variants while preserving message lineage, event diagnostics,
  later-message continuation, exact bytes, decode-cache reuse, and deterministic
  JSON.
- Migrated all six Serialization Composition registrations and Designer
  metadata to canonical ports, explicit concrete factories, and
  `Resources.{name}` clock addresses.
- Moved Serialization to `4.0.0` and Serialization Composition to `2.0.0`,
  updated package docs/changelog/API baseline, and preserved all request-based
  standalone declarations.
- Passed 2,011 Release tests across 63 projects, controlled zero-warning
  Debug/Release builds, binary compatibility, two release preflights/dry-runs,
  and a package-only net8 consumer. Full evidence is in
  [[218-vnext-serialization-flowcontent-flowvalue]].
- Validation followed as the next bounded component-family migration.

## 2026-07-18 - vNext FlowContent Payload Inspection

- Added `FlowContentInspectNode` with exact content preservation, one-time
  cached `FlowValue` reuse, declared JSON/XML/text handling, binary fallback,
  bounded previews, and one normal `FlowResult<PayloadInspectionResult>` output.
- Kept size, decode, parse, null-input, and inspection failures on the normal
  result stream so later messages continue; the canonical node has Events but
  no universal Error port.
- Migrated `payload.inspect` Composition and Designer metadata to canonical
  `FlowContent`/`FlowResult` ports plus optional host-owned codec-catalog and
  clock resources using `Resources.{name}` addresses.
- Moved Payloads to `4.0.0` and Payloads Composition to `2.0.0`, updated package
  docs/changelog/API baseline, and preserved the request-based standalone node.
- Passed 1,998 Release tests across 63 projects, controlled zero-warning
  Debug/Release builds across 130 projects, binary compatibility, two release
  preflights/dry-runs, and a package-only net8 consumer. Full evidence is in
  [[217-vnext-payloads-flowcontent]].
- Serialization followed as the next bounded component-family migration.

## 2026-07-18 - vNext FlowValue Mapping

- Added `FlowValueMapperNode` with exact immutable FlowValue context, compile-once
  expressions, and one normal `FlowResult<FlowValue>` output for success and
  expected failure variants; no JSON round trip or universal Error/Failed port.
- Made parameterless `RegisterMapper()` the canonical Composition registration,
  preserved explicit generic registrations, and migrated Designer metadata to
  canonical ports and `Resources.{name}` resource addresses.
- Moved Mapping to `4.0.0` and Mapping Composition to `2.0.0`, updated package
  docs/changelog/API baseline, and kept prior binary declarations compatible.
- Passed 1,989 Release tests across 63 projects, controlled zero-warning
  Debug/Release builds across 130 projects, binary compatibility, two release
  preflights/dry-runs, and a package-only net8 consumer. Full evidence is in
  [[216-vnext-mapping-flowvalue]].
- Payloads is the next separately bounded component-family migration.

## 2026-07-18 - vNext MQTT Composition

- Added canonical nested MQTT resource binding for brokers, logical clients,
  subscriptions, retry policy, credentials, certificates, Last Will, and
  host-owned transport/controller services with strict reference validation.
- Replaced the legacy Composition surface with `mqtt.control`, `mqtt.publish`,
  `mqtt.trigger`, and `mqtt.events`; expected failures remain normal results,
  while trigger Ack/Nak are payload-independent signal inputs.
- Added message/signal port metadata to Composition, Engine stable signal
  mailboxes and direct access, Designer signal hints, canonical sample/docs,
  and deterministic rejection of duplicate subscription leaf names.
- Hardened prepared Engine output activation against a source/staging fault
  propagation race found by the complete test sweep; the regression passed
  30/30 stress iterations.
- Passed 1,983 Release tests across 63 projects, controlled Debug/Release
  builds across 130 projects, four release preflights and dry-runs, expected
  package compatibility review, and a package-only net8 consumer. Full evidence
  is in [[215-vnext-mqtt-composition]].
- Component-family migration is the next bounded milestone.

## 2026-07-17 - vNext MQTT transport adapters

- Added thin concrete implementations of the provider-neutral MQTT transport
  SPI while keeping reconnect, retry, desired subscriptions, trigger claims,
  ordering, and workflow acknowledgement in the core controller.
- Added exact-byte/configuration mapping, bounded provider event streams,
  deferred broker acknowledgement tokens, stable transport failure
  classification, and coordinated one-outcome broker acknowledgement across
  overlapping trigger matches.
- Moved the concrete adapter packages to `1.2.0` and `2.1.0`, retained their
  legacy APIs, and added one shared behavioral conformance suite alongside
  provider-focused tests.
- Passed 82 MQTT core, 37 first-adapter, 24 second-adapter, 7 shared adapter,
  10 Composition, and 93 release tests; the complete Release sweep passed
  1,977 tests across 63 projects. Controlled Debug/Release builds, binary
  compatibility, release preflight/dry-runs, and a package-only consumer also
  passed. Full evidence is in [[214-vnext-mqtt-adapters]].
- Canonical MQTT Composition binding is the next bounded milestone.

## 2026-07-17 - vNext MQTT core

- Added provider-neutral broker/client configuration, discriminated client
  requests and normal result values, reusable FlowContent MQTT messages, a
  concrete-adapter transport SPI, and one host-lifetime controller per logical
  client.
- Added `MqttControlNode`, `MqttPublishOperationNode`,
  `MqttSubscriptionTriggerNode`, and `MqttClientEventsNode` with normal result
  errors, standard diagnostic events, semantic concurrency/order settings,
  scalar-or-array named/inline subscriptions, and payload-independent Ack/Nak
  signals.
- Implemented auto-connect/reconnect policy, desired-subscription restoration,
  exclusive trigger claims, overlapping-filter delivery deduplication,
  workflow/broker acknowledgement modes, bounded event/trigger isolation, and
  deterministic cleanup while retaining legacy 4.x declarations for the
  adapter migration.
- Moved MQTT core to `5.0.0`. Passed 78 focused MQTT tests, 1,963 Release tests
  across 63 projects, zero-warning Debug/Release builds, 93 release convention
  tests, binary compatibility against `4.1.4`, release preflight, complete
  local-source dry-run, and a package-only consumer that printed
  `MQTT_CORE_API_OK`. Full evidence is in [[213-vnext-mqtt-core]].
- Concrete adapter SPI implementations are the next bounded MQTT milestone;
  canonical Composition binding remains a separate following gate.

## 2026-07-17 - vNext transactional application revisions

- Added deterministic complete-definition revision planning with nested
  resource flattening, transitive dependency closure, missing/cycle
  diagnostics, structural JSON comparison, and whole-workflow replacement
  units.
- Added serialized stable-port revisions with generation-safe input pause,
  immutable output route snapshots, bounded prepared staging, atomic current
  revision publication, and reliable revision events on
  `System.Events.Output`.
- Added an Engine-independent Hosting coordinator that prepares candidates
  off-route, commits one active snapshot after activation, preserves the old
  candidate on pre-commit failure, and reports post-commit drain/disposal
  failures without rollback.
- Passed 123 Composition, 96 Engine, 38 Hosting, and 93 Release tests; 1,943
  Release tests across 63 projects; controlled zero-warning Debug/Release
  builds; binary compatibility, preflight, and local-source package dry-runs
  for all three changed packages; and a package-only net8 consumer that printed
  `TRANSACTIONAL_REVISION_API_OK`. Full evidence is in
  [[212-vnext-transactional-revisions]].
- MQTT is the next bounded vNext vertical slice. Dynamic port registration,
  type migration, automatic mapping, and component state migration remain
  separate later work.

## 2026-07-17 - vNext DI resource and provider snapshots

- Added immutable host/resource-revision/workflow-revision Microsoft DI
  snapshots with copied service descriptors, validation-safe defaults,
  optional scopes, stable metadata, and explicit external-provider bridges.
- Added canonical keyed registration for resources, `Workflow.Component`
  blocks, typed Dataflow ports, and payload-independent `IFlowSignalTarget`
  inputs. Owned, view, and external registrations now have distinct disposal
  behavior.
- Kept Composition.Hosting standalone-first: release boundary tests rejected
  the initial Engine-owned signal contract, so the final signal abstraction
  lives in Nodes and Hosting remains Engine-free.
- Passed 41 Nodes, 116 Composition, 32 Hosting, and 93 Release tests; 1,926
  Release tests across 63 projects; controlled Debug/Release builds; binary
  compatibility, preflight, and local-source dry-runs for the three changed
  packages; and a package-only net8 consumer that printed
  `DI_SNAPSHOT_API_OK`. Full evidence is in
  [[211-vnext-di-resource-provider-snapshots]].
- Transactional resource/workflow revisions are the next bounded milestone;
  MQTT remains a separate later vertical slice.

## 2026-07-17 - vNext system events, diagnostics, and status

- Added the reserved `System.Events.Output` and `System.Diagnostics.Output`
  stable outputs plus exact compiler metadata. Reliable system events are
  ordered/backpressured; best-effort diagnostics reject overflow immediately.
- Added transport-safe event/diagnostic records, component/link failure
  mapping, recursion guards, runtime/port status snapshots, and isolated
  `ILogger`, `ActivitySource`, `Meter`, and `DiagnosticSource` integration.
- Unexpected component source/target faults now detach only that attachment and
  leave the application runtime active; normal completion drains accepted
  system records before closing the reserved outputs.
- Passed 92 Engine, 116 Composition, 17 Hosting, and 93 Release tests, including
  deterministic signal JSON contracts; 1,911 Release tests across the complete
  solution; controlled Debug/Release builds; package compatibility/preflight/
  dry-run; and a package-only net8 API consumer. Full evidence is recorded in
  [[210-vnext-system-events-diagnostics]].
- Keyed DI resource/provider snapshots are the next bounded milestone;
  transactional revisions and MQTT remain separate later stages.

## 2026-07-17 - vNext stable port runtime

- Added additive Engine stable input mailboxes and output broadcast hubs over
  canonical addresses and `FlowMessage<T>`, with explicit typed registration,
  compiled-link activation, and generation-safe target/source attachment.
- Added direct send, receive, bounded observe, and trace-correlated
  request/reply APIs. Expected capacity, availability, completion, and timeout
  states are results; address/type mistakes remain programming errors.
- Isolated condition failures, target rejection/full state, source faults, and
  observation overflow while preserving sibling fan-out and shared-input
  lifetime. Added a bounded best-effort rejection stream as a precursor to full
  system events and diagnostics.
- Moved Engine to additive `2.1.0`, documented the new namespace, and accepted
  the reviewed Engine baseline change from 407 to 503 declarations without
  altering the legacy definition runtime.
- Passed 77 Engine, 116 Composition, 17 Hosting, and 93 Release tests; the
  complete Release solution sweep; controlled Debug/Release builds; binary
  compatibility against local Engine `2.0.3`; release preflight; an isolated
  package dry-run; and a compiled/executed net8 stable-port API consumer.
- Isolated runtime status, `System.Events.Output`, and
  `System.Diagnostics.Output` are the next bounded milestone.

## 2026-07-17 - vNext Composition link compilation

- Added canonical link parsing for string, object, and mixed-array declarations
  on registered input or output properties, with absolute address
  normalization and declaration-side preservation.
- Added compile-once Mapping conditions, per-link evaluation failure capture,
  exact component/port/type validation, duplicate detection, explicit
  single-link claims, host-supplied system-output type metadata, and
  cross-workflow cycle diagnostics.
- Moved Composition to additive `2.1.0`, updated package/docs/changelog records,
  and accepted the reviewed baseline change from 210 to 256 declarations.
- Passed 116 Composition tests, 17 Hosting tests, 63 Engine tests, 97 Designer
  tests, 93 release tests, the complete Release test sweep, controlled
  Debug/Release builds, binary compatibility against local `2.0.0`, release
  preflight, and an isolated net8 package dry-run from a seeded temp source.
- Stable Engine ports and direct send/receive/observe APIs are the next bounded
  milestone; canonical links are not activated in this pass.

## 2026-07-17 - vNext Composition definition and addressing

- Added immutable canonical application, workflow, component, resource-group,
  and resource-instance definitions with exactly `Resources` and `Workflows`.
- Added strict deterministic JSON, root/named-section configuration loading,
  and one ordinal address type for resources, workflow ports, local port
  resolution, and reserved system outputs.
- Kept current runtime DTOs available as an explicit legacy migration surface;
  canonical link properties are preserved but not compiled in this milestone.
- Moved Composition to `2.0.0`, documented the public contract, and accepted
  the reviewed source-declaration baseline change from 155 to 210 declarations.
- Passed 101 Composition tests, 17 Hosting tests, 93 release tests, the complete
  Release test sweep, controlled Debug/Release builds, binary compatibility
  against published `1.2.0`, release preflight, and an isolated net8 consumer
  dry-run from a temporary local dependency source.
- Link normalization and condition compilation are the next bounded milestone.

## 2026-07-17 - vNext data foundation

- Added Dataflow-free `FluxFlow.Data` `1.0.0` with immutable `FlowValue`,
  deterministic canonical JSON, lazy codec-driven `FlowContent`, and shared
  success/error result contracts.
- Updated `FluxFlow.Nodes` to `2.0.0`; `FlowMessage<T>` now carries strong trace
  and message identities, causation, and immutable `FlowValue` headers.
- Added architecture and contract records, package/solution wiring, tests,
  changelog entries, and reviewed public API baseline changes.
- Passed 24 Data tests, 39 Nodes tests, 92 release tests, the complete Release
  test sweep, controlled Debug/Release builds, package creation, and release
  preflight and isolated consumer dry-runs for Data and Nodes.
- Stopped at the foundation API-review gate. Configuration, runtime/DI,
  component migration, diagnostics, and MQTT work remain separate milestones.

## 2026-07-17 - vNext data foundation API review

- Audited package boundaries, every required `FlowValue` kind and invariant,
  `FlowContent` codec/encoding/cache behavior, message identity propagation,
  result semantics, JSON contracts, and major-version impact.
- Added culture-independent literal snapshots, invalid-content and numeric
  failure regressions, encoding precedence coverage, timestamp coverage, and a
  release guard for the dependency-free Data package boundary.
- Passed 32 Data tests, 41 Nodes tests, 93 release tests, the complete Release
  solution test sweep, controlled Debug/Release builds, release preflight, and
  isolated net8 package consumer dry-runs.
- Accepted the foundation API. Canonical Composition definitions and addressing
  are now unblocked as the next separately planned milestone.

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

- Standalone-node re-architecture is now merged to `main`, tagged, and published
  (full detail in [[139-standalone-node-architecture]]). The old
  `work/http-simplify` branch is stale: it has no commits that `origin/main`
  lacks. Current tags at `main` include `nodes-v1.0.0`, `mapping-v1.0.0`,
  `engine-v2.0.0`, `components-requestreply-v1.0.0`,
  `components-http-aspnetcore-v1.0.0`, and the engine-free component package
  `3.0.0` tags. Verified current `main` with
  `dotnet test FluxFlow.sln --configuration Release`, then a no-build TRX run:
  742 tests passed, 0 failed, 0 skipped.
- Installed local knowledge-graph maintenance hooks and kept the generated
  `graphify-out/` directory local-only through `.git/info/exclude`. The hook
  maintains code-derived graph output after local commits/checkouts; run the
  incremental graph update manually after documentation or memory edits.
- Started the MQTT connection simplification pilot on
  `work/mqtt-connection-pilot`: MQTT publish/trigger nodes no longer create
  clients or depend on connection helpers, factories, profiles, leases, or
  adapter composition. `MqttPublishNode` depends only on `IMqttPublisher`,
  `MqttTriggerNode` depends only on `IMqttTriggerSource`, and optional health
  uses `IMqttClientHealthSource`. The old MQTT-specific request/reply helper
  folder was removed; trigger request/reply is now a correlated
  `MqttTriggerResponse` sent to `MqttTriggerNode.Responses`, with ack-on-emit
  or ack-on-success behavior through `IMqttReceivedContext`. The generic
  request/reply package now exposes `CorrelatedRequestTracker`, and both
  `RequestReplyCoordinator` and `MqttTriggerNode` use it for pending correlation,
  duplicate detection, timeout, and shutdown cleanup. Publish transport
  correlation moved from top-level `MqttPublishRequest.CorrelationId` to
  `MqttPublishRequest.Properties.CorrelationId`, so `FlowMessage.CorrelationId`
  remains the workflow correlation source. `MqttPublishOptions.DefaultTopic` was
  removed so publish topics are explicit per request. `MqttPublishOptions`
  later shed quality-of-service and retain defaults too; those are now
  request-owned MQTT publish semantics, while publish options only keep timeout
  and bounded capacity. The package-owned MQTT topic validator now covers
  trigger `TopicFilter` validation too, keeping protocol rules in the core
  package while leaving stricter broker/library policy to adapters. MQTT Last
  Will was recorded as a future adapter/client-
  session configuration concern, not a core publish/trigger node option. Review
  cleanup added constructor validation for publish/trigger static options,
  removed the stale trigger-invalid-topic error code, renamed the surviving
  health event constant to `mqtt.client.healthChanged`, and documented the
  adapter-owned client-session boundary. The duplicate `MqttDiagnosticNames`
  constants were removed, leaving `MqttEventNames` as the single MQTT event-name
  surface. The core MQTT package project was moved to
  `src/Mqtt/FluxFlow.Components.Mqtt` and the solution gained an `Mqtt` folder
  under `src`, leaving package id, assembly, namespace, and public contracts
  unchanged for future MQTT-related libraries to sit beside it. Focused
  RequestReply tests passed at 15 tests and focused MQTT tests passed at 48
  tests; release convention tests passed at 33 tests and full Release solution
  verification passed after the layout move. `git diff --check` passed after
  the MQTT review cleanup. `graphify update . --force` refreshed local graph
  output to 7631 nodes, 11491 edges, and 729 communities.
  See
  [[141-mqtt-connection-simplification-pilot]].
- Added the first concrete MQTT adapter package on
  `work/mqtt-connection-pilot`: `FluxFlow.Components.Mqtt.MqttNet` under
  `src/Mqtt/FluxFlow.Components.Mqtt.MqttNet`, plus
  `tests/FluxFlow.Components.Mqtt.MqttNet.Tests`. `MqttNetClient` owns MQTTnet
  client creation, explicit `ConnectAsync`/`DisconnectAsync`, Last Will
  configuration, reconnect/resubscribe behavior, publish mapping, trigger
  subscription streams, manual acknowledgement hooks, and health events while
  implementing the neutral `IMqttPublisher`, `IMqttTriggerSource`, and
  `IMqttClientHealthSource` contracts. Registered the package in
  `eng/packages.json`, added it to `FluxFlow.sln`, updated `CHANGELOG.md`, and
  documented usage in the package README. Verification: adapter build passed,
  focused MQTT tests passed at 48, focused adapter tests passed at 19, release
  convention tests passed at 33, and the full Release solution test passed after
  rerunning one transient existing Nodes test. See
  [[142-mqttnet-adapter-package]].
- Refreshed local graph output after the MQTTnet adapter package and memory
  updates with `graphify update . --force`: 7783 nodes, 11712 edges, and
  740 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- Added the second concrete MQTT adapter package on
  `work/mqtt-connection-pilot`: `FluxFlow.Components.Mqtt.PulseMqtt` under
  `src/Mqtt/FluxFlow.Components.Mqtt.PulseMqtt`, plus
  `tests/FluxFlow.Components.Mqtt.PulseMqtt.Tests`. `PulseMqttClient` wraps
  Pulse `ResilientMqttClient`, owns TCP/TLS or injected Pulse transport
  configuration, exposes `StartAsync`/`StopAsync` plus connected-waiting
  `ConnectAsync`, maps publish requests, route-stream trigger subscriptions,
  Last Will, and health events to the neutral MQTT contracts, and keeps strict
  disconnected publish behavior by default unless the caller explicitly opts
  into Pulse's offline queue. Manual broker acknowledgement modes are rejected
  because Pulse route streams manage acknowledgement internally. Registered the
  package in `eng/packages.json`, added it to `FluxFlow.sln`, updated
  `CHANGELOG.md`, and documented usage in the package README. Verification:
  adapter build passed, focused MQTT tests passed at 48, focused MQTTnet tests
  passed at 19, focused Pulse tests passed at 8, release convention tests passed
  at 33, and the full Release solution test passed. See
  [[143-pulsemqtt-adapter-package]].
- Refreshed local graph output after the Pulse MQTT adapter package and memory
  updates with `graphify update . --force`: 7938 nodes, 11960 edges, and
  759 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- Updated the FluxFlow Pulse MQTT adapter to target stable upstream Pulse MQTT
  `2.0.0` (`Pulse.Mqtt.Client` and `Pulse.Mqtt.Testing`). The adapter now uses
  the v2 `OpenRouteStream` API and keeps broker subscription ownership on the
  explicit `SubscribeAsync` call. Verification passed: Pulse adapter build,
  Pulse adapter tests (`8`), core MQTT tests (`48`), and release convention
  tests (`33`). See [[145-fluxflow-pulsemqtt-v2-adoption]].
- Refreshed local graph output after the Pulse MQTT `2.0.0` adapter adoption
  with `graphify update . --force`: 7950 nodes, 11971 edges, and
  753 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- Restored the minimal one-line route convenience upstream in Pulse MQTT
  (`D:\Projects\MqttNg`) for the local `2.1.0` development line:
  `ResilientMqttClient.OnAsync(...)` now registers a local raw/typed handler,
  subscribes the route's broker filter, and returns `MqttSubscribedRoute` so
  disposal unregisters locally and unsubscribes from the broker. The explicit v2
  model remains the advanced contract. Verification passed with a clean Release
  build, 442 non-soak/non-broker-matrix tests, and the docs build from
  `docs/`. Refreshed local graph output with `graphify update . --force`. See
  [[146-pulsemqtt-onasync-convenience]].
- Released the upstream `OnAsync(...)` convenience as stable Pulse MQTT
  `2.1.0`: PR #97 merged, tag `v2.1.0` was pushed, workflow run `27873206048`
  passed, the release attached all nine package artifacts, and public feed
  flat-container checks returned `200` for every `2.1.0` package. Opened the
  next upstream development cycle through PR #98 (`2.2.0` on `main`);
  workflow run `27873384358` passed and all nine `2.2.0-preview.69` packages
  indexed. FluxFlow dependency adoption remains a separate decision. See
  [[146-pulsemqtt-onasync-convenience]].
- Added the next upstream Pulse MQTT route ergonomics helper locally on
  `feature/route-template-subscribe`: route-template `SubscribeAsync(...)`
  extension overloads allow parsed `MqttRouteTemplate` subscriptions with QoS
  and cancellation while delegating to the existing broker filter subscription
  path. Hidden string-template detection was avoided. Verification passed with
  the client build, client tests (`89`), full Release build, broad
  non-soak/non-broker-matrix tests (`442`), and docs build. See
  [[147-pulsemqtt-route-template-subscribe-helper]].
- Released the route-template `SubscribeAsync(...)` helper as stable Pulse MQTT
  `2.2.0`: PR #99 merged, tag `v2.2.0` was pushed, release workflow run
  `27875265109` passed, GitHub release
  `https://github.com/araxis/pulse-mqtt/releases/tag/v2.2.0` was created, and
  all nine stable packages indexed on NuGet. The broker matrix had one transient
  HiveMQ shared-subscription timeout before rerunning green. PR #100 then opened
  the `2.3.0` development cycle on `main`; release workflow run `27875467096`
  passed and all nine `2.3.0-preview.72` packages indexed. See
  [[147-pulsemqtt-route-template-subscribe-helper]].
- Refreshed local graph output after recording the upstream Pulse MQTT `2.2.0`
  release and `2.3.0-preview.72` publish with `graphify update . --force`:
  7962 nodes, 11983 edges, and 753 communities. `graph.html` was skipped because
  the graph exceeds the local HTML visualization limit.
- Released the next upstream Pulse MQTT durable storage add-on:
  `Pulse.Mqtt.Storage.LiteDB` exposes `LiteDbMessageStore` and
  `LiteDbSessionStore` over the same `IMessageStore` / `ISessionStore`
  contracts as SQLite, keeps LiteDB details internal through `BsonDocument`
  rows and a shared serialized store gate, and updates solution, package docs,
  resilience/migration docs, changelog, and the release workflow package list.
  Verification passed with the LiteDB package build, LiteDB tests (`21`), full
  Release build, broad non-soak/non-broker tests (`463`), package creation for
  ten packages including `Pulse.Mqtt.Storage.LiteDB.2.3.0.nupkg`, docs build,
  PR #101 checks, stable `v2.3.0` release workflow run `27876350812`, and NuGet
  indexing for all ten `2.3.0` packages. PR #102 opened `2.4.0`; workflow run
  `27876562110` published `2.4.0-preview.75` for all ten packages after a rerun
  of one existing chaos integration flake. See
  [[148-pulsemqtt-litedb-storage-package]].
- Refreshed local graph output after recording the Pulse MQTT LiteDB storage
  package memory with `graphify update . --force`: 7966 nodes, 11987 edges, and
  755 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- Refreshed local graph output after recording the upstream Pulse MQTT `2.3.0`
  stable release and `2.4.0-preview.75` publish with
  `graphify update . --force`: 7967 nodes, 11988 edges, and 762 communities.
  `graph.html` was skipped because the graph exceeds the local HTML
  visualization limit.
- Removed quality-of-service and retain defaults from `MqttPublishOptions` on
  the MQTT pilot. `MqttPublishRequest` now owns the actual MQTT message
  semantics with at-most-once and non-retained defaults, and publish options
  only keep node runtime settings (`PublishTimeoutMilliseconds` and
  `BoundedCapacity`). Verification passed for core MQTT tests (`48`), MQTTnet
  adapter tests (`19`), Pulse MQTT adapter tests (`8`), and release convention
  tests (`33`).
- Prepared the MQTT pilot package release set: RequestReply `1.1.0`, core MQTT
  `4.0.0`, and initial MQTTnet/Pulse MQTT adapter packages `1.0.0`. Release
  preflight and fast package dry-runs passed for all four packages, and full
  solution Release tests passed before merge/publish.
- Merged and published the MQTT pilot package release set. PR #24 merged into
  `main` with squash commit `118a06de613a9ebdfd47e9e06b7c6761161a4d37`.
  Stable releases were created for `FluxFlow.Components.RequestReply` `1.1.0`,
  `FluxFlow.Components.Mqtt` `4.0.0`,
  `FluxFlow.Components.Mqtt.MqttNet` `1.0.0`, and
  `FluxFlow.Components.Mqtt.PulseMqtt` `1.0.0`. The core MQTT and adapter
  release workflows needed dependency-order reruns because newly published
  dependencies were not immediately visible on NuGet; the reruns passed and
  explicit public-feed verification returned `FEED_OK` for all four packages.
- Refreshed local graph output after the publish-options cleanup with
  `graphify update . --force`: 7966 nodes, 11986 edges, and 764 communities.
  `graph.html` was skipped because the graph exceeds the local HTML
  visualization limit.
- Refreshed local graph output after MQTT pilot release prep and version bumps
  with `graphify update . --force`: 7968 nodes, 11988 edges, and
  756 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- Refreshed local graph output after recording the merged MQTT pilot release
  with `graphify update . --force`: 7908 nodes, 11897 edges, and
  749 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- Released upstream Pulse MQTT `2.4.0` from `D:\Projects\MqttNg` with manual
  inbound broker acknowledgement support: `OpenAcknowledgedRouteStream(...)`,
  `MqttAcknowledgedRoutedMessage`, `CanReject`, lossless route overflow
  enforcement, and QoS 1/2 ack/reject tests. Commit `99963b4`, tag `v2.4.0`,
  release workflow run `27880444942`, and all ten packages indexed on NuGet.
- Adopted Pulse MQTT `2.4.0` in `FluxFlow.Components.Mqtt.PulseMqtt`. The
  adapter package is bumped to `1.1.0`; `Acknowledgement.None` still uses
  managed route-stream acknowledgement, while `OnEmit` and
  `OnSuccessfulResponse` now use `OpenAcknowledgedRouteStream(...)` and
  delegate `IMqttReceivedContext.AckAsync` / `NackAsync` to Pulse. Verification
  passed for the adapter build, Pulse adapter tests (`9`), core MQTT tests
  (`48`), release convention tests (`33`), package release preflight, and a
  temporary `dotnet pack` check for the `1.1.0` `.nupkg` / `.snupkg`.
- Refreshed local graph output after adopting Pulse MQTT `2.4.0` in the
  FluxFlow adapter with `graphify update . --force`: 7921 nodes, 11917 edges,
  and 750 communities. `graph.html` was skipped because the graph exceeds the
  local HTML visualization limit.
- Implemented the MQTT DI and adapter-owned feature plan. Core MQTT stays pure
  at `4.0.0` with no capability descriptor and no umbrella registration
  package. The MQTTnet adapter moves to `1.1.0` and adds adapter-local
  `AddFluxFlowMqttClient(...)`, keyed registrations for `MqttNetClient`,
  `IMqttPublisher`, `IMqttTriggerSource`, and `IMqttClientHealthSource`, plus
  optional hosted connect/disconnect lifetime through
  `MqttClientRegistrationOptions`. The Pulse adapter keeps `1.1.0` and adds the
  same adapter-local registration shape for `PulseMqttClient`, optional hosted
  startup through `MqttClientRegistrationOptions`, and optional Pulse
  message/session store hooks. Message stores require
  `AllowOfflinePublishQueue = true` so strict disconnected publish behavior is
  not accidentally bypassed. Verification passed for MQTTnet adapter build,
  Pulse adapter build, core MQTT tests (`48`), Pulse adapter tests (`12`),
  MQTTnet adapter tests (`22`), release convention tests (`33`), full solution
  Release tests, and package release preflight for `components-mqtt-mqttnet`
  and `components-mqtt-pulsemqtt` `1.1.0`.
  See
  [[150-mqtt-di-and-adapter-owned-features]].
- Refreshed local graph output after the MQTT DI and adapter-owned feature
  implementation with `graphify update . --force`: 7989 nodes, 11992 edges, and
  757 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- Updated `FluxFlow.Components.Mqtt.PulseMqtt` to stable upstream Pulse MQTT
  `2.5.0`. MQTTnet was already current at `5.1.0.1559`; only
  `Pulse.Mqtt.Client` and `Pulse.Mqtt.Testing` moved from `2.4.0` to `2.5.0`.
  The adapter now calls the upstream `ConnectAsync` / `DisconnectAsync`
  lifecycle APIs internally while preserving FluxFlow's adapter-level
  `StartAsync` / `StopAsync` host lifecycle helpers. Verification passed for
  Pulse adapter restore, Release build, Pulse adapter tests (`12`), core MQTT
  tests (`48`), release convention tests (`33`), and package release preflight
  for `components-mqtt-pulsemqtt` (`1.1.0`). Local graph output was refreshed:
  7995 nodes, 11998 edges, and 756 communities; `graph.html` was skipped
  because the graph exceeds the local HTML visualization limit. See
  [[151-pulsemqtt-2.5-lifecycle-update]].
- Ran final adapter `1.1.0` release verification. Full solution Release tests
  passed, and package dry-runs passed for both `components-mqtt-mqttnet`
  (`DRY_RUN_OK=FluxFlow.Components.Mqtt.MqttNet`) and
  `components-mqtt-pulsemqtt`
  (`DRY_RUN_OK=FluxFlow.Components.Mqtt.PulseMqtt`). Each dry-run produced the
  `.nupkg` / `.snupkg`, ran consumer smoke, and returned feed verification OK.
  Memory updates were refreshed with `graphify update . --force`: 7995 nodes,
  11998 edges, and 755 communities; `graph.html` was skipped because the graph
  exceeds the local HTML visualization limit.
- Changed the MQTTnet adapter registration default so `ConnectWithHost` is
  `false` unless the composition layer explicitly opts into hosted
  connect/disconnect. Updated the MQTTnet README, changelog, and DI tests.
  Verification passed for MQTTnet adapter tests (`23`), release convention tests
  (`33`), and `components-mqtt-mqttnet` `1.1.0` package dry-run with
  `DRY_RUN_OK=FluxFlow.Components.Mqtt.MqttNet`.
  Refreshed local graph output with `graphify update . --force`: 7996 nodes,
  12001 edges, and 749 communities; `graph.html` was skipped because the graph
  exceeds the local HTML visualization limit.
- Implemented `FluxFlow.Composition` v1 as the standalone-first composition
  layer. The new package references `FluxFlow.Nodes`, not `FluxFlow.Engine`;
  exposes composition DTOs, JSON options, fluent builders, an
  `IConfiguration` loader, explicit `CompositionNodeRegistry` factory
  registration, port metadata, structural validation, direct typed Dataflow
  linking, `CompositionRuntimeBuilder`, `CompositionRuntime`, build diagnostics,
  event/error aggregation, cleanup on factory/build failure, and reload-facing
  source/planner contracts. Added focused composition tests (`12`) for fluent
  definitions, config loading, validation errors, runtime lifecycle, factory
  cleanup, and fluent/config workflow equivalence; added a pure in-memory
  `samples/FluxFlow.CompositionSample`; updated solution, release manifest,
  changelog, package README, root README, and docs entrypoints to make
  standalone-node-first official and engine optional. Verification passed for
  full solution Debug build, composition tests, release convention tests, the
  full no-build solution test suite, and the sample run. See
  [[152-standalone-composition-layer]].
- Refreshed local graph output after the standalone composition layer
  implementation with `graphify update . --force`: 8317 nodes, 12404 edges, and
  799 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- Implemented `FluxFlow.Composition.Hosting` v1 as the optional DI/host bridge
  for standalone compositions. The package references `FluxFlow.Composition`,
  not `FluxFlow.Engine`; registers a single composition runtime with
  `IServiceCollection`; loads definitions from a static object,
  `IConfiguration`, or exact configuration section; builds through
  `CompositionRuntimeBuilder`; starts/stops through `IHostedService`; exposes
  diagnostics through `ICompositionRuntimeHost`; supports configurable
  build-failure behavior and host start/stop behavior; and adds
  `CompositionNodeFactoryContext` resource helpers that resolve
  `NodeDefinition.Resources` entries from keyed DI services. Added focused
  tests (`5`) for keyed resource orchestration, configuration loading, manual
  start, non-throwing diagnostics, and missing resource build failure. Updated
  solution, release manifest, changelog, package README, root README, and docs.
  Verification passed for full solution Debug build, composition hosting tests,
  composition tests, release convention tests, and full no-build solution tests.
  See [[153-composition-hosting-layer]].
- Refreshed local graph output after the composition hosting layer
  implementation with `graphify update . --force`: 8456 nodes, 12587 edges, and
  814 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- Implemented `FluxFlow.Components.Mqtt.Composition` v1 as the optional
  composition adapter for MQTT standalone nodes. The package references core
  MQTT, `FluxFlow.Composition`, and `FluxFlow.Composition.Hosting`; registers
  explicit `mqtt.publish` and `mqtt.trigger` factories; binds existing MQTT
  node options from composition configuration; and resolves keyed
  `IMqttPublisher`, `IMqttTriggerSource`, and optional `TimeProvider` resources
  without moving broker/client ownership into composition. Added package tests
  (`4`), solution/manifest/changelog/docs wiring, and local package dry-run
  verification. See [[154-mqtt-composition-adapter]].
- Refreshed local graph output after the MQTT composition adapter
  implementation with `graphify update . --force`: 8529 nodes, 12682 edges, and
  824 communities. `graph.html` was skipped because the graph exceeds the local
  HTML visualization limit.
- Added `samples/FluxFlow.MqttCompositionSample`, a broker-free hosted
  composition sample that registers one in-memory object as keyed
  `IMqttTriggerSource` and `IMqttPublisher`, runs
  `mqtt.trigger -> sample.mqtt.reply -> mqtt.publish`, and demonstrates both
  `appsettings.json` configuration and fluent definitions over the same
  factories. Verification passed for the sample run, full solution Debug build,
  release convention tests (`33`), and full no-build solution tests.
- Refreshed local graph output after adding the MQTT composition sample with
  `graphify update . --force`: 8563 nodes, 12734 edges, and 831 communities.
  `graph.html` was skipped because the graph exceeds the local HTML
  visualization limit.
- Recorded the current composition and Designer progress snapshot. Since the
  first MQTT composition adapter, optional `.Composition` adapters have been
  added for the normal standalone component families: HTTP, Mapping, Control,
  Assertions, Validation, Timers, Sources, Routing, Serialization, Payloads,
  Observability, Projections, Metrics, Expectations, FileSystem, State,
  Storage, Sessions, and MQTT. Request/reply remains intentionally skipped as a
  normal component-family adapter, and Journal remains support-only.
- Recorded the Designer boundary and metadata state. Designer is now engine
  neutral, package-owned metadata providers exist across composition packages,
  shared metadata helpers and validation are in place, and the latest local work
  added richer option/resource hints first to Mapping and then to Control. The
  suggested next narrow pass is Assertions metadata hints. See
  [[155-composition-and-designer-progress]].
- Refreshed local graph output after the memory snapshot update: 11609 nodes,
  19587 edges, and 1081 communities. The local HTML graph was skipped because
  the graph exceeds the visualization size limit.
- Completed the Assertions Designer metadata hint pass. The Assertions
  composition provider now includes option section, importance, editor, syntax,
  and related-resource hints, plus host-owned resource key patterns for
  `engine`, `contextFactory`, and `clock`. The Assertions composition package is
  bumped to `1.3.0`; README, changelog, and focused metadata tests were updated.
  Verification passed for Assertions composition tests (`12`), Designer tests
  (`93`), release tests (`84`), and the controlled solution build with 0
  warnings and 0 errors. Local graph output was refreshed after the memory
  closeout: 11622 nodes, 19610 edges, and 1076 communities. See
  [[156-assertions-designer-metadata-hints]].
- Completed the State Designer metadata hint pass. The State composition
  provider now includes option section, importance, editor, syntax, and
  related-resource hints for the state reducer node, plus host-owned resource
  key patterns for `engine` and `clock`. The State composition package is
  bumped to `1.3.0`; README, changelog, and focused metadata tests were
  updated. Verification passed for State composition tests (`15`), Designer
  tests (`93`), release tests (`84`), and the controlled solution build with 0
  warnings and 0 errors. Local graph output was refreshed after the memory
  closeout. See [[157-state-designer-metadata-hints]].
- Completed the Observability Designer metadata hint pass. The Observability
  composition provider now includes option section, importance, editor, syntax,
  and related-resource hints for Counter, Logger, and Metrics, plus host-owned
  resource key patterns for expression engine, context factory, selector, and
  clock resources. The Observability composition package is bumped to `1.3.0`;
  README, changelog, and focused metadata tests were updated. Verification
  passed for Observability composition tests (`22`), Designer tests (`93`),
  release tests (`84`), and the controlled solution build with 0 warnings and
  0 errors after shutting down stale build servers from the timed-out first
  attempt. Local graph output was refreshed after the memory closeout. See
  [[158-observability-designer-metadata-hints]].
- Completed the Validation Designer metadata hint pass. The Validation
  composition provider now includes option section, importance, editor, and
  related-resource hints for the JSON schema validator node, plus host-owned
  resource key patterns for `selector` and `clock`. The Validation composition
  package is bumped to `1.3.0`; README, changelog, and focused metadata tests
  were updated. Verification passed for Validation composition tests (`15`),
  Designer tests (`93`), release tests (`84`), and the controlled solution
  build with 0 warnings and 0 errors. Local graph output was refreshed after
  the memory closeout. See
  [[159-validation-designer-metadata-hints]].
- Completed the Routing Designer metadata hint pass. The Routing composition
  provider now includes option section, importance, editor, syntax, and
  related-resource hints for Switch, Fork, Merge, Window, Correlation, and Join,
  plus host-owned resource key patterns for selector delegate and clock
  resources. The Routing composition package is bumped to `1.3.0`; README,
  changelog, and focused metadata tests were updated. Verification passed for
  Routing composition tests (`17`), Designer tests (`93`), release tests
  (`84`), and the controlled solution build with 0 warnings and 0 errors.
  Local graph output was refreshed after the memory closeout. See
  [[160-routing-designer-metadata-hints]].
- Completed the Timers Designer metadata hint pass. The Timers composition
  provider now includes option section, importance, and editor hints for
  Interval, Schedule, Delay, Throttle, and Debounce, plus a host-owned resource
  key pattern for the `clock` resource. The Timers composition package is
  bumped to `1.5.0`; README, changelog, and focused metadata tests were
  updated. Verification passed for Timers composition tests (`14`), Designer
  tests (`93`), release tests (`84`), and the controlled solution build with 0
  warnings and 0 errors. Local graph output was refreshed after the memory
  closeout. See [[161-timers-designer-metadata-hints]].
- Completed the Sources Designer metadata hint pass. The Sources composition
  provider now includes option section, importance, and editor hints for
  Generated Source and Sequence Source metadata, plus a host-owned resource key
  pattern for the `clock` resource. The Sources composition package is bumped
  to `1.4.0`; README, changelog, and focused metadata tests were updated.
  Verification passed for Sources composition tests (`22`), Designer tests
  (`93`), release tests (`84`), and the controlled solution build with 0
  warnings and 0 errors after a scoped build-server shutdown and rerun. Local
  graph output was refreshed after the memory closeout. See
  [[162-sources-designer-metadata-hints]].
- Completed the Serialization Designer metadata hint pass. The Serialization
  composition provider now includes option section, importance, and editor
  hints for the shared JSON/Text/Base64 option surface, plus a host-owned
  resource key pattern for the `clock` resource. The Serialization composition
  package is bumped to `1.3.0`; README, changelog, and focused metadata tests
  were updated. Verification passed for Serialization composition tests (`16`),
  Designer tests (`93`), release tests (`84`), and the controlled solution
  build with 0 warnings and 0 errors. Local graph output was refreshed after
  the memory closeout. See [[163-serialization-designer-metadata-hints]].
- Completed the Payloads Designer metadata hint pass. The Payloads composition
  provider now includes option section, importance, and editor hints for
  `payload.inspect`, plus a host-owned resource key pattern for the `clock`
  resource. The Payloads composition package is bumped to `1.3.0`; README,
  changelog, and focused metadata tests were updated. Verification passed for
  Payloads composition tests (`13`), Designer tests (`93`), release tests
  (`84`), and the controlled solution build with 0 warnings and 0 errors.
  Local graph output was refreshed after the memory closeout. See
  [[164-payloads-designer-metadata-hints]].
- Completed the Projections Designer metadata hint pass. The Projections
  composition provider now includes option section, importance, and editor
  hints for `event.projection`, plus a host-owned resource key pattern for the
  `clock` resource. The Projections composition package is bumped to `1.3.0`;
  README, changelog, and focused metadata tests were updated. Verification
  passed for Projections composition tests (`11`), Designer tests (`93`),
  release tests (`84`), and the controlled solution build with 0 warnings and
  0 errors. Local graph output was refreshed after the memory closeout. See
  [[165-projections-designer-metadata-hints]].
- Completed the Metrics Designer metadata hint pass. The Metrics composition
  provider now includes option section, importance, and editor hints for
  `metrics.aggregate`, plus a host-owned resource key pattern for the `clock`
  resource. The Metrics composition package is bumped to `1.3.0`; README,
  changelog, and focused metadata tests were updated. Verification passed for
  Metrics composition tests (`13`), Designer tests (`93`), release tests
  (`84`), and the controlled solution build with 0 warnings and 0 errors.
  Local graph output was refreshed after the memory closeout. See
  [[166-metrics-designer-metadata-hints]].
- Completed the Expectations Designer metadata hint pass. The Expectations
  composition provider now includes option section, importance, and editor
  hints for `event.expectation`, plus a host-owned resource key pattern for the
  `clock` resource. The Expectations composition package is bumped to `1.3.0`;
  README, changelog, and focused metadata tests were updated. Verification
  passed for Expectations composition tests (`15`), Designer tests (`93`),
  release tests (`84`), and the controlled solution build with 0 warnings and
  0 errors after a scoped build-server shutdown and rerun. Local graph output
  was refreshed after the memory closeout. See
  [[167-expectations-designer-metadata-hints]].
- Completed the HTTP Designer metadata hint pass. The HTTP composition provider
  now includes option section, importance, and editor hints for `http.client`,
  plus host-owned resource key patterns for the required `client` and optional
  `clock` resources. The HTTP composition package is bumped to `1.3.0`;
  README, changelog, and focused metadata tests were updated. Verification
  passed for HTTP composition tests (`14`), Designer tests (`93`), release
  tests (`84`), and the controlled solution build with 0 warnings and 0 errors.
  Local graph output was refreshed after the memory closeout. See
  [[168-http-designer-metadata-hints]].
- Completed the FileSystem Designer metadata hint pass. The FileSystem
  composition provider now includes option section, importance, and editor hints
  for `file.read`, `file.write`, `directory.enumerate`, and `file.watch`, plus
  a host-owned resource key pattern for the optional `clock` resource. The
  FileSystem composition package is bumped to `1.4.0`; README, changelog, and
  focused metadata tests were updated. Verification passed for FileSystem
  composition tests (`27`), Designer tests (`93`), release tests (`84`), and
  the controlled solution build with 0 warnings and 0 errors after stopping one
  lingering FluxFlow-owned build process and shutting down build servers. Local
  graph output was refreshed after the memory closeout. See
  [[169-filesystem-designer-metadata-hints]].
- Completed the Storage Designer metadata hint pass. The Storage composition
  provider now includes option section, importance, and editor hints for
  `storage.put`, `storage.get`, `storage.query`, and `storage.delete`, plus
  host-owned resource key patterns for the required `store` and optional
  `clock` resources. The Storage composition package is bumped to `1.4.0`;
  README, changelog, and focused metadata tests were updated. Verification
  passed for Storage composition tests (`20`), Designer tests (`93`), release
  tests (`84`), and the controlled solution build with 0 warnings and 0 errors.
  Local graph output was refreshed after the memory closeout. See
  [[170-storage-designer-metadata-hints]].
- Completed the Sessions Designer metadata hint pass. The Sessions composition
  provider now includes option section, importance, and editor hints for
  `session.recorder`, `session.replay`, and `session.query`, plus host-owned
  resource key patterns for the required `store` and optional `clock`
  resources. The Sessions composition package is bumped to `1.5.0`; README,
  changelog, and focused metadata tests were updated. Verification passed for
  Sessions composition tests (`25`), Designer tests (`93`), release tests
  (`84`), and the controlled solution build with 0 warnings and 0 errors after
  build-server shutdown. Local graph output was refreshed after the memory
  closeout. See [[171-sessions-designer-metadata-hints]].
- Completed the MQTT Designer metadata hint pass. The MQTT composition provider
  now includes option section, importance, and editor hints for `mqtt.publish`
  and `mqtt.trigger`, plus host-owned resource key patterns for the required
  `publisher`, required `triggerSource`, and optional `clock` resources. The
  MQTT composition package is bumped to `1.4.0`; README, changelog, and focused
  metadata tests were updated. Verification passed for MQTT composition tests
  (`10`), Designer tests (`93`), release tests (`84`), and the controlled
  solution build with 0 warnings and 0 errors. Local graph output was refreshed
  after the memory closeout. See [[172-mqtt-designer-metadata-hints]].
- Closed the Designer metadata hint convention pass. Release convention tests
  now require option section/importance hints, validate current Designer
  option hint values, require same-node related resources, and require
  host-owned resource key patterns containing `{name}`. No runtime behavior,
  provider metadata content, public APIs, package versions, package docs, or
  changelog entries changed. Verification passed for release tests (`85`),
  Designer tests (`93`), and the controlled solution build with 0 warnings and
  0 errors. Local graph output was refreshed after the memory closeout. See
  [[173-designer-metadata-hint-conventions]].
- Ran the Designer metadata hint release-readiness pass. Release tests
  (`85`), Designer tests (`93`), controlled Debug build, controlled Release
  build, and all impacted package preflights passed. The Designer package
  `2.16.0` fast dry-run passed from a temp package source. The first
  composition dry-run (`components-mapping-composition` `1.3.0`) packed but
  failed consumer restore because current dependency packages such as
  `FluxFlow.Composition` `1.0.9`, `FluxFlow.Composition.Hosting` `1.0.5`,
  `FluxFlow.Mapping` `1.0.2`, and `FluxFlow.Components.Mapping` `3.0.1` are
  not present in the isolated package source or public feed. No package source
  changes were made; a separate dependency-source readiness pass is needed. See
  [[174-designer-metadata-hint-release-readiness]].
- Ran the Designer metadata hint dependency-source readiness pass. A fresh temp
  source outside the repo was seeded with all 55 packages from
  `eng/packages.json` after a controlled Release build. Release tests (`85`),
  Designer tests (`93`), controlled Debug and Release builds, all 20 impacted
  package preflights, and all 20 impacted fast dry-runs passed with the seeded
  source plus the public feed for external dependencies. No package source,
  release script, changelog, README, version, or public API baseline changes
  were made. See [[175-designer-metadata-hint-dependency-source-readiness]].
- Recorded the Designer metadata hint publication-sequencing handoff. The note
  lists the dependency-aware order for `components-designer`, current shared and
  runtime dependency packages, and the metadata hint composition packages.
  Release preflight and tag prepare-only checks passed for all 44 aliases in
  the closure; local and configured-remote tag checks found 42 absent tags and
  2 already-present runtime dependency tags. No tags, pushes, package source,
  release script, changelog, README, version, or public API baseline changes
  were made. See [[176-designer-metadata-hint-publication-sequencing]].
- Ran the final no-publish release rehearsal for the Designer metadata hint
  train. A fresh temp package source was seeded with all 55 packages after a
  controlled Release build. All 44 dependency-ordered aliases passed release
  preflight, fast package dry-run against the seeded source, and tag
  prepare-only checks. Tag checks again found 42 absent tags and 2
  already-present runtime dependency tags. No tags, pushes, package source,
  release script, changelog, README, version, or public API baseline changes
  were made. See [[177-designer-metadata-hint-final-release-rehearsal]].
- Executed the local tag step for the Designer metadata hint train. The
  worktree was clean at release target
  `d7da08e5bad380e243cdd49988808285292d66de`; the controlled Release build
  passed; a fresh temp source was seeded with all 55 packages; and 42 local
  annotated release tags were created at the target commit. The two
  already-present runtime dependency tags were skipped. No tags were pushed,
  no packages were published, and no package source, release script, changelog,
  README, version, or public API baseline files changed. See
  [[178-designer-metadata-hint-local-tag-execution]].
- Pushed the Designer metadata hint release tags. Pre-push verification found
  the 42 local tags on release target
  `d7da08e5bad380e243cdd49988808285292d66de`, absent from the configured
  remote, with the 2 skipped runtime dependency tags already present remotely.
  All 42 tags were pushed in dependency order and their remote peeled targets
  now resolve to the release target. No packages were published, and no
  package source, release script, changelog, README, version, or public API
  baseline files changed. See [[179-designer-metadata-hint-tag-push]].
- Recovered the Designer metadata hint release workflows. The release-test
  project-reference path helper now normalizes both `/` and `\`, fixing the
  Linux-only release-test failure; local release tests passed (`86`), and the
  controlled solution build passed after a scoped build-server shutdown. All
  42 dependency-ordered tags were verified unpublished, retargeted from
  `d7da08e5bad380e243cdd49988808285292d66de` to
  `31800f5b3ecb0a5985e2eb7d32be6dd2d6221f77`, pushed one at a time, and
  watched to successful release workflow completion. Each release has package
  assets and each package version is visible on the public package feed. Three
  workflow runs needed one rerun after transient full-suite test failures, then
  passed before the train continued. See
  [[180-designer-metadata-hint-release-workflow-recovery]].
- Published the current concrete MQTT adapter packages. Pre-release checks
  confirmed both release tags and package-feed versions were absent. Focused
  MQTTnet tests (`33`), Pulse MQTT tests (`22`), core MQTT tests (`58`),
  release tests (`86`), the controlled solution build, package preflights, and
  fast dry-runs passed. Tags `components-mqtt-mqttnet-v1.1.7` and
  `components-mqtt-pulsemqtt-v2.0.7` were created at
  `9108abdf4c1aad1216163dd9ae36c4b51f9055df`, pushed to the configured
  remote, watched through successful first-attempt release workflows, and
  verified with release assets plus public package-feed restores. See
  [[181-mqtt-adapter-package-release]].
- Consumer-validated the published package set from the Designer metadata hint
  release train and current MQTT adapter releases. Release tests (`86`) passed;
  the controlled solution build passed after a scoped build-server shutdown
  cleared stale local output locks; all 44 package-feed checks passed; and a
  temporary `net8.0` consumer project with all 44 direct package references
  restored from the public package feed and built with 0 warnings and 0 errors.
  See [[182-public-package-consumer-validation]].
- Completed a documentation-only package README clarity pass. Inventory found
  all 55 manifest package READMEs present with matching package-ID headings.
  Runtime, composition, adapter, and support-package boundary wording was
  tightened where stale or vague, and the component coverage matrix now records
  the pass as complete. Release tests (`86`), the controlled solution build,
  `git diff --check`, and graph refresh passed. No source APIs, runtime
  behavior, package versions, release notes, changelog entries, public API
  baselines, tags, publishing workflow, or release scripts changed. See
  [[183-package-readme-clarity-pass]].
- Added a package binary compatibility readiness helper. The new release script
  resolves package aliases and versions through the existing manifest/resolver,
  restores the baseline package into the NuGet global package cache outside the
  repo, and runs `dotnet pack --no-build` with SDK package validation enabled.
  Release tests passed at `91`; the controlled Release build passed; manifest
  enumeration found 55 packages; and `components-designer` `2.16.0` passed
  binary compatibility validation against its published same-version baseline.
  The all-package loop stopped at the first missing published same-version
  baseline: `FluxFlow.Components.Configuration` `1.5.0` is not on the public
  feed, with NuGet reporting nearest version `1.0.0`. See
  [[184-package-binary-compat-readiness]].
- Started the binary compatibility baseline feed-alignment release pass. The
  nine missing current-version tags, releases, and feed versions were absent,
  local release tests passed (`91`), and the controlled Release and Debug
  builds passed. `components-http-aspnetcore` `1.0.4` preflight and fast
  dry-run passed, and tag `components-http-aspnetcore-v1.0.4` was pushed at
  `2d24d5b076550281e070294c82cce4fedd6dece9`, but tag workflow run
  `28611193314` failed in the Test step before pack/publish. The Linux runner
  hit a binary-compat release-test fixture CRLF shebang error
  (`/usr/bin/env: 'bash\r': No such file or directory`). No GitHub release or
  package-feed version exists for `FluxFlow.Components.Http.AspNetCore`
  `1.0.4`, the remaining eight package tags were not pushed, and source/tooling
  edits were deferred to a separate recovery pass. See
  [[185-package-binary-compat-baseline-feed-alignment-blocker]].
- Recovered the binary compatibility baseline feed-alignment release pass. The
  release-test fixture now writes the fake Unix `dotnet` script with LF line
  endings and guards the generated shebang script against carriage-return
  bytes. Release tests passed (`92`), and the controlled Release and Debug
  builds passed with 0 warnings and 0 errors. The failed
  `components-http-aspnetcore-v1.0.4` tag was retargeted to
  `a62c96888f92bde4dbe303bb15eac4c1632e8da0`; the remaining eight baseline
  tags were created at the same fixed commit. All nine release workflows
  completed successfully, each release has two assets, every package-feed check
  passed, and all 55 manifest packages passed
  `eng/package-binary-compat-preflight.ps1` against their published
  same-version baselines. See
  [[186-package-binary-compat-feed-alignment-recovery]].
- Consumer-validated the full current manifest package set from the public
  package feed. Release tests passed (`92`), the controlled Debug solution build
  passed with 0 warnings and 0 errors, all 55 package-feed checks passed, and a
  temporary `net8.0` consumer project outside the repository with all 55 direct
  package references restored with `--no-cache` and built in Release
  configuration with 0 warnings and 0 errors. No package source, versions,
  release notes, README files, changelog entries, public API baselines, release
  scripts, tags, or publishing state changed. See
  [[187-full-public-package-consumer-validation]].
- Added neutral Designer resource picker hint contracts.
  `ComponentResourcePickerHint` and `ComponentResourcePickerHints.Create(...)`
  let hosts read host-owned resource picker metadata from one metadata item or a
  validated catalog, including key patterns, related options, required flags,
  value type/display fields, and parsed conditional option names. The Designer
  package moves to `2.17.0`; renderer UI, resource catalogs, keyed resolution,
  resource lifetimes, component metadata content, runtime behavior, and hot
  reload remain out of scope. Designer tests (`97`), release tests (`92`),
  controlled Release and Debug builds, binary compatibility preflight against
  `2.16.0`, package release preflight, and fast release dry-run passed. See
  [[188-designer-resource-picker-hint-contracts]].
- Published `FluxFlow.Components.Designer` `2.17.0`. Pre-release checks
  confirmed the worktree was clean at
  `738f2e1cf38aaff083e6534004a7baa342020904`, the tag
  `components-designer-v2.17.0` was absent locally and on `origin`, and the
  public feed did not yet contain `2.17.0`. Designer tests (`97`), release
  tests (`92`), controlled Release and Debug builds, binary compatibility
  preflight against `2.16.0`, release preflight, and fast dry-run passed. The
  tag was pushed, workflow run `28622249640` completed successfully, the GitHub
  release has `.nupkg` and `.snupkg` assets, local and remote peeled tags point
  at `738f2e1cf38aaff083e6534004a7baa342020904`, and public feed verification
  passed. See [[189-designer-resource-picker-hint-package-release]].
- Consumer-validated the full current manifest package set after publishing
  Designer `2.17.0`. Release tests passed (`92`), the controlled Debug solution
  build passed with 0 warnings and 0 errors, all 55 package-feed checks passed
  against the public package feed, and a temporary `net8.0` consumer project
  outside the repository with all 55 direct package references restored with
  `--no-cache` and built in Release configuration with 0 warnings and 0 errors.
  No package source, versions, release notes, README files, changelog entries,
  public API baselines, release scripts, tags, or publishing state changed. See
  [[190-full-public-package-consumer-validation-after-designer-2-17]].
- Planned the Designer host layer as documentation-only follow-up work. Added
  `docs/18-designer-host-layer.md` to define how a future host can consume
  `ComponentDesignMetadataCatalog`, option hints, resource metadata attributes,
  and `ComponentResourcePickerHints.Create(...)` for palette, inspector,
  resource picker, validation, persistence, and runtime-mapping concerns. No
  source APIs, renderer behavior, resource ownership, hot reload, runtime
  behavior, package versions, tags, or publishing state changed. Release tests
  passed (`92`), and the controlled Debug solution build passed after
  `dotnet build-server shutdown` cleared generated assembly file locks.
  `graphify update . --force` refreshed `graphify-out/` with 12447 nodes,
  22705 edges, and 976 communities; `graph.html` was skipped because the graph
  exceeds the local HTML visualization limit. See
  [[191-designer-host-layer-planning]].
- 2026-07-03: Completed the composition dependency-hygiene pass. Keyed
  resource resolution moved onto `CompositionNodeFactoryContext` instance
  methods in `FluxFlow.Composition` (`1.1.0`), the
  `FluxFlow.Composition.Hosting` context extensions became obsolete delegating
  wrappers (`1.1.0`), all 19 `.Composition` adapters dropped their
  `FluxFlow.Composition.Hosting` reference, and `FluxFlow.Nodes` (`1.2.0`)
  gained `FlowNodeOptions.Clock` for deterministic safety-net error
  timestamps. The public API baseline was re-accepted through the documented
  flow; changelog and package release notes were updated. Local `main` was
  fast-forwarded to `88027c7`; pushing `origin/main` remains an operator step.
  Verification: controlled Release build with 0 warnings/0 errors, release
  tests `92` passed, and the full no-build Release suite `1707` passed across
  59 assemblies. See [[192-composition-resource-helper-relocation]].
- 2026-07-03: Implemented Designer host layer phases 1-2 as the headless
  host-model layer `samples/FluxFlow.DesignerHost` with
  `tests/FluxFlow.DesignerHost.Tests` (20 tests): palette, inspector, option
  editor, and resource picker view models projected from
  `ComponentDesignMetadataCatalog` by a single explicit `DesignerHostCatalog`
  adapter, with conservative editor fallbacks and host-owned-only resource
  prompts. Both projects joined `FluxFlow.sln`; the sample is listed in
  `docs/README.md`, and the coverage matrix candidate note was updated. No
  package source, versions, or publishing state changed. See
  [[193-designer-host-model-layer]].
- 2026-07-03: Implemented Designer host layer phase 4 in
  `samples/FluxFlow.DesignerHost`: `GraphModel` (nodes, raw JSON option
  values, resource references, links with optional cross-workflow segments,
  host-only layout), `GraphDefinitionMapper` with lossless JSON-verified
  round-trips to `CompositionDefinition`, and `ValidationMessageMapper` for
  metadata errors plus composition diagnostics. Host tests now `29` passed.
  Renderer UI is the only remaining Designer host pass. See
  [[194-designer-host-persistence-mapping]].
- 2026-07-03: Added a shared "Fanout" NuGet package icon
  (`assets/icon.svg`/`assets/icon.png`) wired repo-wide through
  `Directory.Build.targets`, minor-bumped the 19
  `FluxFlow.Components.*.Composition` adapters for the
  `FluxFlow.Composition.Hosting` dependency removal plus the icon, and
  published the full 22-package release set (`FluxFlow.Nodes` `1.2.0`,
  `FluxFlow.Composition` `1.1.0`, `FluxFlow.Composition.Hosting` `1.1.0`, and
  the 19 adapters). The `main` branch push stayed permission-blocked but
  `work/designer-host-model` pushed successfully, unblocking release tags.
  First-pass downstream workflows hit the known nuget.org indexing-lag flake
  (`NU1102` at the pre-publish smoke gate, nothing partially published);
  re-running after each dependency indexed brought all 22 packages to
  `success`. Verified live: all 22 on the flat-container index, the embedded
  icon endpoint returns `200`, and a fresh temporary consumer project
  referencing all 22 packages restored and built cleanly. See
  [[195-nuget-icon-and-hygiene-release-prep]].
- 2026-07-03: Extended the shared icon to the remaining 33 manifest packages
  (patch-only: Designer `2.17.1`, all core non-.Composition component
  packages, `Mapping` `1.0.3`, `Engine` `2.0.2`, and the two MQTT adapters) so
  all 55 current packages carry it. Released in 3 dependency-derived waves
  (17/14/2). Updated one release-notes fixture test that hardcoded content
  tied to `Configuration`'s previous version. Hit a second pre-existing flaky
  test (`Source_EmitAsync_WaitsWhenBoundedOutputIsFull` in
  `FluxFlow.Nodes.Tests`) on 4 of the first 31 release runs; confirmed
  unrelated to any session change (2/5 local isolated reruns failed), got user
  approval to auto-retry that exact signature, and all affected releases
  passed on retry. Verified: all 55 packages independently confirmed on the
  nuget.org flat-container index, icon endpoint returns `200`, and a fresh
  temporary consumer referencing all 55 packages restored and built cleanly.
  See [[196-full-icon-rollout-completion]].
- 2026-07-03: Fixed the second flaky test found during the icon release wave.
  `FlowMultiOutputAndSourceTests.Source_EmitAsync_WaitsWhenBoundedOutputIsFull`
  asserted an un-observable BroadcastBlock internal-scheduling race; diagnosed
  the latest-wins/coalescing behavior empirically (two wrong fix attempts
  failed 28/30 and 40/40 before the correct diagnosis), then rewrote it as
  `Source_EmitAsync_DeliversLatestThroughBoundedOutputAndCompletes` verifying
  the design's real contract (ordered delivery, final value always arrives,
  source completes). Passes 60/60 in isolation; full `FluxFlow.Nodes.Tests`
  suite `36` passed. Test-only change; no `FluxFlow.Nodes` source or package
  version changed. See [[197-bounded-source-flaky-test-fix]].
- 2026-07-03: Merged the accumulated work into `main` via PR #54 (clean
  fast-forward, tags preserved by using a merge commit) and started the Designer
  host layer renderer UI (docs/18 phase 5) on branch `work/designer-renderer-ui`
  as `samples/FluxFlow.DesignerApp` — a Blazor WebAssembly + MudBlazor app
  (net10.0) over `FluxFlow.DesignerHost`. First slice: component palette and
  option/resource inspector from the real metadata catalog. Browser-verified via
  the preview tooling (palette 23 components in 8 categories; `timer.interval`
  inspector shows 6 options + the Clock resource with correct editors, ordering,
  required/advanced markers, and picker/value-type/key-pattern). Added to
  `FluxFlow.sln` and `docs/README.md`; full Release solution build and `92`
  release tests pass. Canvas, persistence, and validation display are follow-on
  slices. See [[198-designer-renderer-app-first-slice]].
- 2026-07-03: Added the node canvas slice to `samples/FluxFlow.DesignerApp`
  using `Z.Blazor.Diagrams` `3.0.4.1` (API verified by reflecting over the
  packaged assemblies since no XML docs ship). `Features/Designer/Canvas/`:
  `FlowNodeModel : NodeModel` (component identity + input/output ports),
  `DesignerGraphState` (owns the single `BlazorDiagram` and selection).
  `DesignerPage` is now a three-pane palette | canvas | inspector layout;
  palette click adds a node, canvas selection drives the inspector, plus
  zoom-to-fit and an empty-state prompt. Browser-verified: adding
  `timer.interval` and `storage.put` renders two titled nodes; selecting a node
  switches the inspector to its component type. Full `FluxFlow.sln` Release
  build clean. See [[199-designer-renderer-canvas-slice]].
- 2026-07-03: Completed the docs/18 phase 5 renderer with the persistence
  slice. `DesignerGraphMapper` maps the `BlazorDiagram` to/from the host-model
  `GraphModel` (link endpoints resolved back to named ports); `DesignerGraphState`
  gained `ToJson`/`LoadJson`/`Clear` over `GraphDefinitionMapper` and
  `CompositionDefinitionJson`; a `GraphJsonDialog` plus Save/Load/Clear toolbar
  actions surface load warnings/errors via `ISnackbar`. Added a
  serialize/deserialize round-trip test to the DesignerHost tests
  (DesignerHost suite `30` passed). Browser-verified full round-trip: two nodes
  -> Save (valid composition JSON) -> Clear -> Load restores both nodes with a
  success snackbar. Release tests `92` passed; full Release solution build
  clean. See [[200-designer-renderer-persistence-slice]].
- 2026-07-03: Made the renderer produce configured compositions. The inspector
  option editors now write into the selected `FlowNodeModel.Configuration`
  (seed on load, write on change via `@bind-Value:after`); `DesignerGraphMapper`
  emits/consumes `GraphNodeModel.Options`. Editors are `@key`-ed by node so
  switching selection reseeds them. Browser-verified: set a timer's `Interval`
  to `00:00:05` -> Save carries `configuration.interval` -> Clear -> Load
  restores it and the inspector shows it. Full Release solution build clean.
  See [[201-designer-renderer-option-editing]].
- 2026-07-03: Merged the renderer to `main` via PR #55 (clean fast-forward;
  build-test CI green, confirming the WASM app builds on the Linux runner).
  Fixed a palette layout bug (MudDivider ships `flex-grow:1`; as a direct
  flex-column child it grew to ~422px and pushed the list to the bottom — wrapped
  the header block so the list fills). Then added editor polish on branch
  `work/designer-editor-polish`: `DesignerGraphState.DeleteSelected` + a Delete
  toolbar button and link-creation validation (reject self-links and
  non-output→input via `link.TargetAttached`, with an `ISnackbar` reason).
  Browser-verified: delete removes the selected node; a valid output→input drag
  creates `interval-2.Output -> putRecord-3.Input`; an output→output drag is
  rejected with the snackbar and not persisted. See
  [[202-designer-renderer-editor-polish]].
- 2026-07-09: Closed eight runtime/component review findings. Composition now
  coordinates fan-in completion and aggregates cleanup failures; Engine fanout
  is bounded and startup cancellation is consistent; confined FileSystem paths
  reject linked descendants and reads enforce streaming limits; debounce/window
  timer races emit exactly once; and HTTP honors response charsets. Seven patch
  versions were prepared locally. Focused suites, release tests (`92`),
  controlled Debug/Release builds, binary compatibility, release preflight, and
  all seven local-source package dry-runs passed. No public baseline, adapter
  version, tag, publication, PR, or merge changed. See
  [[204-runtime-and-component-review-fixes]].

## Remaining

- The Designer metadata hint release train is published, indexed, and
  consumer-validated. Designer now also has neutral resource picker hint
  contracts published in `FluxFlow.Components.Designer` `2.17.0`; the Designer
  host layer is planned in `docs/18-designer-host-layer.md`, and any renderer
  UI prototype or package-family work should be a separate bounded pass.
- The current concrete MQTT adapter package updates are published, indexed, and
  consumer-validated. Future MQTT adapter work should be planned as a separate
  bounded pass.
- The package README clarity pass is complete. Future documentation work should
  be scoped to a concrete stale section, package family, or user-facing
  publication requirement.
- Package binary compatibility preflight tooling exists, the missing current
  baseline package versions are published, and same-version binary
  compatibility preflight passed for all 55 manifest packages. Future package
  release readiness should include the helper after a controlled Release build.
- All 55 current manifest packages are public-feed visible and validated by a
  combined temporary consumer restore/build after the Designer `2.17.0`
  publication. Future consumer validation should be rerun after package version
  changes or publication batches.
- Composition adapter packages are bumped for the
  `FluxFlow.Composition.Hosting` dependency removal (release prep done in
  `195`); the bumped versions are validated by dry-run but not yet published.
  Publishing the release set (3 core + 19 adapters) via
  `eng/package-release-tag.ps1 -Push` in dependency-wave order is the pending
  operator step, blocked on syncing `origin/main`.
- Keep future work bounded: one package family, one convention pass, or one
  release-readiness pass per local commit, with focused tests, release
  convention tests, and the controlled solution build.
- Keep local graph output updated after repo changes and keep it out of git.
  See [[140-local-graph-maintenance]].
- Hot reload semantics, renderer behavior, resource catalog UI, runtime
  lifecycle hooks, and request/reply redesign remain deferred until separately
  planned.
- `FluxFlow.Fluent` DSL built on branch `work/fluent-dsl` (plan in `203`):
  `Flow.From(source).Then(node).Tap(side).Branch(port, sub).To(sink).Build()`
  with compile-time-checked wiring, fan-out, branching from typed ports, and
  fan-in (share a node instance across branches). Reuses `CompositionRuntime`
  via a new additive public seam `CompositionRuntime.Create(nodes, links,
  entryNodes)` (Composition `1.1.0 -> 1.2.0`). Links are wired without
  `PropagateCompletion`; each node completes when all upstreams finish (correct
  fan-in). Shipped as `FluxFlow.Fluent 1.0.0` (manifest, changelog, docs/14,
  baseline `55|21`, README, shared icon) with `samples/FluxFlow.FluentSample`.
  9 tests (branch/fan-in flake-checked 30x), full release suite green (92),
  Release pack verified. Merged to `main` via PR #57 (CI green) and released to
  nuget.org: `composition-v1.2.0` and `fluent-v1.0.0` (both published + indexed,
  GitHub releases created; consumer smoke/feed-verify green, no indexing flake).
  Follow-on shipped as `FluxFlow.Fluent 1.1.0` (PR #59, `fluent-v1.1.0`,
  published + indexed): `OnError`/`OnEvent` observation on
  `FlowBuilder`/`FlowTerminal`/`FlowGraph` over the aggregated error/event
  broadcasts (handler-isolated, torn down with the graph; 6 new tests, baseline
  `55|29`).
- Hosting shipped as new package `FluxFlow.Fluent.Hosting 1.0.0` (PR #61,
  `fluent-hosting-v1.0.0`, published + indexed): `services.AddFlowGraph(sp =>
  Flow…Build())` registers a `FlowGraph` as an `IHostedService` (start on host
  start, drain on stop, dispose on shutdown); the factory delegate also gives
  DI-resolved nodes. 5 tests (flake-checked 20x), baseline index 56.
- Reusable named sub-flows shipped as `FluxFlow.Fluent 1.2.0` (PR #63,
  `fluent-v1.2.0`, published + indexed): `FlowSegment<TIn,TOut>`
  (`FlowSegment.Define`) + `FlowBuilder.Apply(segment)`; the segment holds a
  build delegate (not node instances) so each application makes fresh nodes,
  reusable across graphs. 6 new tests (flake-checked 20x), baseline `55|35`. The
  planned fluent-DSL feature set is now complete; only builder DI factory
  overloads remain deliberately unbuilt (KISS, redundant given the hosting
  factory).
