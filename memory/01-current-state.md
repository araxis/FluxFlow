# Current State

Date: 2026-07-20

## Repository

- `D:\Projects\FluxFlow` is currently on local branch
  `work/application-runtime-generations`. The provider-neutral MQTT vertical slice and the
  bounded normal component-family migrations through Sessions, canonical FlowValue Mapping,
  FlowContent Payloads, FlowContent/FlowValue Serialization, FlowValue
  Validation, FlowValue Assertions, projection-event Expectations, and
  FlowValue/result Routing plus canonical-link Control deprecation and
  FlowValue State commands/results, typed-event Projection results, typed
  Metric snapshot/results, FlowValue Observability results, canonical
  FlowValue Generated/Sequence sources, and canonical FlowValue/result Timers
  plus exact-content HTTP polymorphic results, exact-content FileSystem
  results/sources, exact-content Storage, and exact-content Sessions are
  implemented and verified locally.
  Nothing from the vNext milestones was pushed, tagged, published, merged, or
  opened as a pull request.
- `graphify-out/` is local-only and excluded through `.git/info/exclude`; it is
  not part of the tracked repository state.
- Added Dataflow-free `FluxFlow.Data` `1.0.0` for immutable `FlowValue`,
  `FlowContent` and codecs, and result/error contracts. `FluxFlow.Nodes` is now
  `2.1.0`; its `FlowMessage<T>` envelope uses strong trace/message identity,
  causation, and immutable `FlowValue` headers, and `IFlowSignalTarget` provides
  payload-independent signal intake without an Engine dependency. The manifest
  contains 58 packages. Controlled Debug/Release builds, the complete Release
  test sweep, package creation, release preflight, and isolated consumer
  dry-runs passed.
  The reviewed foundation now supports the canonical Composition definition,
  address, link-compilation, stable-port, system-signal, DI-snapshot, and
  transactional-revision milestones. MQTT, Mapping, Payloads, Serialization,
  Validation, Assertions, Expectations, Routing, Control, State, and
  Projections, Metrics, and Observability now use this foundation. The ordinary
  component-family migrations through Sessions and resource/configuration
  alignment, canonical Hosting, canonical Designer persistence, and coordinated
  all-package source and consumer validation are complete.
  See
  `205-vnext-data-foundation.md` and
  `206-vnext-data-foundation-api-review.md`.
- `FluxFlow.Composition` is now `2.5.0` locally with immutable canonical
  `Resources`/`Workflows` definitions, strict deterministic JSON and
  configuration loading, nested resource namespaces, and one ordinal,
  case-sensitive address type. Canonical input/output-side links now normalize
  to absolute addresses, preserve declaration side, compile conditions once,
  and report exact endpoint/type, duplicate, exclusive-claim, and cycle
  diagnostics. Composition ports now distinguish typed messages from
  payload-independent signals, support reflection-free generic type dispatch,
  and canonical component factory contexts bind flat settings and resource
  references directly. The existing executable
  Composition DTOs remain a documented migration surface. Focused and
  complete tests, controlled Debug/Release builds, binary compatibility,
  release preflight, and isolated package dry-run passed. See
  `207-vnext-composition-definition-addressing.md` and
  `208-vnext-composition-link-compilation.md`.
- `FluxFlow.Engine` is now `2.6.0` locally with additive canonical stable input
  mailboxes and output broadcast hubs, revision-safe typed attachment,
  compiled-link activation, explicit direct send/receive/observe/request-reply
  result contracts, bounded rejection reporting, canonical system-event and
  diagnostic outputs, component-fault isolation, and runtime/port status
  snapshots. It now also supports serialized stable-port revisions with
  generation-safe input replacement, immutable output route snapshots,
  bounded prepared staging, atomic revision publication, and reliable revision
  events. Stable signal mailboxes now accept any `FlowMessage<T>` payload and
  are addressable through the same runtime and keyed-service surfaces. Prepared
  output activation also observes source faults directly, removing an
  asynchronous Dataflow propagation race. Accepted diagnostics integrate with
  standard .NET logging, activity,
  metric, and diagnostic-source surfaces. Focused Engine,
  Composition, Hosting, and release tests passed; complete build, package, and
  consumer evidence is recorded in `209-vnext-stable-port-runtime.md` and
  `210-vnext-system-events-diagnostics.md`.
- Engine now also provides the standard canonical runtime assembler over
  explicit Composition node registrations and DI service contributors. It
  creates candidate-owned resource/workflow providers, validates component
  descriptors, activates compiled links and stable port attachments as one
  revision, and exposes the current port generation through
  `IApplicationRuntimeAccess`. Exact-surface revisions retain stable direct
  handles; component add/remove or payload-type changes atomically publish an
  isolated generation while the prior candidate drains. JSON start, direct
  send/receive, resource-backed replacement, cleanup, and generation retirement
  are covered in `238-canonical-application-runtime-assembly.md` and
  `239-application-runtime-port-generations.md`.
- `FluxFlow.Composition.Hosting` is now `2.2.0` locally with immutable owned
  host/resource-revision/workflow-revision providers and canonical keyed
  registration for resources, `Workflow.Component` blocks, typed Dataflow
  ports, and `IFlowSignalTarget`. Exact external instances/providers cross the
  boundary only through explicit non-owning bridges; snapshots do not scan,
  merge, or fall back to another provider. Focused and full tests, controlled
  builds, binary compatibility, package preflight/dry-runs, and a package-only
  net8 consumer passed. It now also provides an Engine-independent
  `ApplicationRevisionCoordinator` that plans complete definitions, prepares
  candidates off-route, publishes one active snapshot after activation, and
  drains replaced candidates without rolling back post-commit failures. See
  `211-vnext-di-resource-provider-snapshots.md` and
  `212-vnext-transactional-revisions.md`.
- `FluxFlow.Components.Mqtt` is now `5.0.0` locally with provider-neutral
  endpoint/client configuration, one controller per logical client,
  discriminated command/result contracts, FlowContent application messages,
  transport SPI, semantic request scheduling, named/inline subscriptions,
  workflow and broker acknowledgement policy, lifecycle diagnostics, and
  reconnect/restoration behavior. Expected failures are normal result values;
  the vNext MQTT nodes expose Output and Events without universal Error or
  State ports. Legacy 4.x declarations remain temporarily for migration
  compatibility. Both concrete packages now implement the transport SPI as
  thin provider sessions: `FluxFlow.Components.Mqtt.MqttNet` is `1.2.0` and
  `FluxFlow.Components.Mqtt.PulseMqtt` is `2.1.0`. Shared conformance,
  focused/full tests, controlled builds, binary compatibility, release
  preflight/dry-runs, and package-only consumers passed. Canonical MQTT
  Composition is now `2.0.0` with flat/nested `mqtt.broker`, `mqtt.client`,
  `mqtt.subscription`, and `resilience.retry` resources, one keyed controller
  per client, four fixed node contracts, Ack/Nak signal inputs, strict resource
  validation, and Designer metadata. `FluxFlow.Components.Designer` is now
  `2.18.0` with message/signal port hints. Focused and complete tests,
  controlled builds, package checks, and a package-only consumer passed. The
  broader component-family migration began with Mapping. See
  `213-vnext-mqtt-core.md`, `214-vnext-mqtt-adapters.md`, and
  `215-vnext-mqtt-composition.md`.
- `FluxFlow.Components.Mapping` is now `4.0.0` locally with a canonical
  `FlowValueMapperNode` that consumes `FlowValue` without serialization and
  emits success or expected failure as one `FlowResult<FlowValue>` output.
  `FluxFlow.Components.Mapping.Composition` is `2.0.0`; parameterless
  `RegisterMapper()` owns the canonical `flow.mapper` contract and Designer
  metadata, while explicit generic registration preserves the prior typed
  surface. Focused/full tests, zero-warning controlled builds, binary
  compatibility, release preflight/dry-runs, and a package-only consumer
  passed. Payloads followed as the next bounded family. See
  `216-vnext-mapping-flowvalue.md`.
- `FluxFlow.Components.Payloads` is now `4.0.0` locally with a canonical
  `FlowContentInspectNode` that preserves exact content, exposes its cached
  decoded `FlowValue`, trusts declared media conventions, and emits size,
  decode, parse, and inspection failures as one normal
  `FlowResult<PayloadInspectionResult>` output. The request-based
  `PayloadInspectNode` remains available for code-authored compatibility.
  `FluxFlow.Components.Payloads.Composition` is `2.0.0` with canonical ports,
  no universal error output, and optional host-owned codec-catalog and clock
  resources. Focused/full tests, zero-warning controlled builds, binary
  compatibility, release preflight/dry-runs, and a package-only consumer
  passed. Serialization followed as the next bounded family. See
  `217-vnext-payloads-flowcontent.md`.
- `FluxFlow.Components.Serialization` is now `4.0.0` locally with six canonical
  standalone nodes for explicit JSON, text, and Base64 conversion between
  `FlowContent` and `FlowValue`. Expected conversion failures remain normal
  `FlowResult<T>` data; exact bytes, decode-cache reuse, deterministic JSON,
  configured limits, encoding fallback, message lineage, and Events are
  preserved. All request-based standalone nodes remain available for
  code-authored compatibility. `FluxFlow.Components.Serialization.Composition`
  is `2.0.0` with canonical fixed ports, explicit concrete factories, no
  universal Errors port, and a host-owned `Resources.{name}` clock. Focused and
  full tests, zero-warning controlled builds, binary compatibility, release
  preflight/dry-runs, and a package-only consumer passed. Validation followed
  as the next bounded family. See
  `218-vnext-serialization-flowcontent-flowvalue.md`.
- `FluxFlow.Components.Validation` is now `4.0.0` locally with a canonical
  `FlowValueJsonSchemaValidatorNode` that emits valid and invalid evaluations,
  plus expected selector and schema failures, as one normal
  `FlowResult<JsonSchemaFlowValueValidationResult>` stream. The transport-neutral
  selector returns `FlowValue`, deterministic conversion supplies ordinary JSON
  schema semantics, and message lineage is preserved. The generic standalone
  node and contracts remain available for code-authored compatibility.
  `FluxFlow.Components.Validation.Composition` is `2.0.0` with canonical fixed
  ports, no legacy branch or universal Errors ports, and host-owned
  `Resources.{name}` selector and clock addresses. Focused/full tests,
  zero-warning controlled builds, binary compatibility, release
  preflight/dry-runs, and a package-only consumer passed. Assertions followed
  as the next bounded family. See `219-vnext-validation-flowvalue.md`.
- `FluxFlow.Components.Assertions` is now `4.0.0` locally with a canonical
  `FlowValueAssertionNode` that emits passed and failed rules as successful
  `FlowResult<FlowValueAssertionResult>` variants and missing/evaluation
  failures as normal error results. It preserves exact values, expression
  metadata, timing, diagnostics, message lineage, and later-message
  continuation without branch or universal Errors ports. The generic
  standalone component and branch/error contracts remain available for
  code-authored compatibility. `FluxFlow.Components.Assertions.Composition` is
  `2.0.0` with canonical fixed ports, parameterless registration, and
  host-owned `Resources.{name}` engine, FlowValue context factory, and clock
  addresses. Focused/full tests, zero-warning controlled builds, binary
  compatibility, release preflight/dry-runs, and a package-only consumer
  passed. Expectations followed as the next bounded family. See
  `220-vnext-assertions-flowvalue.md`.
- `FluxFlow.Components.Expectations` is now `4.0.0` locally with canonical
  `FlowEventExpectationNode`. It emits matched, unmet, timeout, and ordered
  completion as successful `FlowResult<EventExpectationResult>` variants and
  expected filter evaluation failure as one normal error result. Exact-once
  arbitration preserves deterministic clocks, retained projection-event
  evidence, diagnostics, and strong message lineage without a universal Errors
  port. The released standalone node and direct-result/error contract remain
  available for code-authored compatibility. Expectations Composition is now
  `2.0.0` with canonical fixed ports and exact `Resources.{name}` clock
  addressing. Focused/full tests, controlled builds, binary compatibility,
  preflight/dry-runs, and a package-only consumer passed. Routing followed as
  the next bounded family. See `221-vnext-expectations-flowresult.md`.
- `FluxFlow.Components.Routing` is now `4.0.0` locally with canonical
  `FlowValueWindowNode`, `FlowValueCorrelationNode`, and `FlowValueJoinNode`.
  Windows, matches, and timeouts are successful `FlowResult<T>` variants;
  expected selector, key, side, and capacity failures are normal error variants
  with preserved message lineage and no universal Errors port. Switch, Fork,
  and Merge remain compatible but are obsolete because canonical links own
  conditional routing, fan-out, and shared-input fan-in. Routing Composition is
  now `2.0.0` with parameterless canonical registration, fixed FlowValue/result
  ports, exact keyed selectors/clocks, and deprecated structural metadata.
  Focused/full tests, controlled builds, binary compatibility,
  preflight/dry-runs, and a package-only consumer passed. Control followed as
  the next bounded family. See `222-vnext-routing-flowvalue.md`.
- `FluxFlow.Components.Control` is now `4.0.0` locally and
  `FluxFlow.Components.Control.Composition` is `2.0.0`. Filter and When remain
  compatible but are obsolete because canonical conditioned output links own
  filtering and branching. Their legacy factories and complete Designer
  metadata remain available; Designer entries are deprecated so hosts can hide
  them from new-node palettes while rendering stored definitions. No runtime
  behavior or public declaration signature changed. Focused/full tests,
  controlled builds, binary compatibility, preflight/dry-runs, and a
  package-only consumer passed. State followed as the next bounded family.
  See `223-vnext-control-link-deprecation.md`.
- `FluxFlow.Components.State` is now `4.0.0` locally with canonical typed
  commands carrying immutable FlowValue input/state and one normal
  `FlowResult<FlowValueStateReducerResult>` Output. Updated, reset, and cleared
  operations are success variants; expected message, key, expression, reducer,
  and key-limit failures are normal error variants with stable string codes.
  The object-based standalone node remains available for compatibility. State
  Composition is now `2.0.0` with canonical fixed ports, no universal Errors
  surface, ordinary JSON initial-state decoding, and exact host-owned engine and
  clock resources. Focused/full tests, controlled builds, binary compatibility,
  preflight/dry-runs, and a package-only consumer passed. Projections followed
  as the next bounded family. See `224-vnext-state-flowvalue.md`.
- `FluxFlow.Components.Projections` is now `4.0.0` locally with canonical
  `FlowEventProjectionNode`. It retains typed ProjectionEvent and snapshot
  contracts while emitting matching/final snapshots and expected failures
  through one `FlowResult<EventProjectionSnapshot>` Output plus Events.
  Ordered counts, filters, previews, replay-time rates, fan-out, and message
  lineage are preserved; configured final snapshots emit after normal input
  drain. The direct-result standalone node remains available for compatibility.
  Projections Composition is now `2.0.0` with the canonical fixed Output, no
  universal Errors surface, and an exact host-owned clock resource. Focused/full
  tests, controlled builds, binary compatibility, preflight/dry-runs, and a
  package-only consumer passed. Metrics is the next bounded family assessment.
  See `225-vnext-projections-flowresult.md`.
- `FluxFlow.Components.Metrics` is now `4.0.0` locally with canonical
  `FlowMetricsAggregateNode`. It retains typed metric sample and snapshot
  contracts while emitting per-sample/final snapshots, partial group-limit
  applications, and expected failures through one
  `FlowResult<MetricSnapshotOutput>` Output plus Events. Ordered aggregation,
  rates, fan-out, and message lineage are preserved; coalesced final snapshots
  emit after normal input drain. The direct-result standalone node remains
  available for compatibility. Metrics Composition is now `2.0.0` with the
  canonical fixed Output, no universal Errors surface, and an exact host-owned
  clock resource. Focused/full tests, controlled builds, binary compatibility,
  preflight/dry-runs, and a package-only consumer passed. Observability is the
  next bounded family assessment. See `226-vnext-metrics-flowresult.md`.
- `FluxFlow.Components.Observability` is now `4.0.0` locally with canonical
  FlowValue Counter, Logger, and Metrics nodes. Each emits one normal FlowResult
  Output plus Events; rejected Counter inputs are explicit successes, and
  Logger/Metrics selector failures are one partial result carrying usable data.
  FlowValue-native selectors avoid object conversion, and Logger accepts scalar
  or array selector-name configuration. All released generic nodes remain
  available for compatibility. Observability Composition is now `2.0.0` with
  parameterless canonical registrations, explicit generic compatibility
  overloads, no canonical Errors surfaces, and exact host-owned resources.
  Focused/full tests, controlled builds, binary compatibility,
  preflight/dry-runs, and a package-only consumer passed. Sources is the next
  bounded family assessment. See `227-vnext-observability-flowvalue.md`.
- `FluxFlow.Components.Sources` is now `4.0.0` locally with canonical
  FlowValue Generated and Sequence sources. Both retain natural zero-input
  lifecycle, deterministic timing, bounded output, fresh message identity,
  Events, and no universal Errors port. Ordinary JSON `items` binds as one
  value or an array in Composition. Released typed standalone nodes remain
  available, and explicit typed Composition paths preserve generated and
  `SourceSequenceItem` contracts. Sources Composition is now `2.0.0` with
  parameterless canonical registrations, fixed FlowValue metadata, and an exact
  host-owned clock resource. Focused/full tests, controlled builds, binary
  compatibility, preflight/dry-runs, and a package-only consumer passed. See
  `228-vnext-sources-flowvalue.md`.
- `FluxFlow.Components.Timers` is now `4.0.0` locally with canonical FlowValue
  Interval and Schedule sources plus FlowValue-to-FlowResult Delay, Throttle,
  and Debounce transforms. Timer sources retain natural lifecycle and fresh identity;
  transforms preserve ordered temporal semantics, normal expected failures,
  exact-once debounce completion, later-input continuation, and strong lineage.
  Released typed nodes remain available unchanged. Timers Composition is now
  `2.0.0` with parameterless canonical registrations, explicit typed
  compatibility paths, fixed Designer ports, no canonical Errors surfaces, and
  an exact host-owned clock resource. Focused/full tests, controlled builds,
  binary compatibility, preflight/dry-runs, and a package-only consumer passed.
  See `229-vnext-timers-flowvalue-results.md`.
- `FluxFlow.Components.Http` is now `4.0.0` locally with canonical
  `HttpClientRequest`, exact FlowContent request/response bodies, and
  polymorphic `HttpClientResult` response/error outcomes. Expected request,
  timeout, transport, response-read, and configured status failures remain
  normal data on one Output; host-owned client policy, bounded response reads,
  diagnostics, and strong message lineage are preserved. The released
  `HttpClientNode` and request/response contracts remain unchanged. HTTP
  Composition is now `2.0.0` with canonical fixed ports, no universal Errors
  surface, unchanged keyed client/clock ownership, and explicit typed
  compatibility registration. Focused/full tests, controlled builds, binary
  compatibility, preflight/dry-runs, and a package-only consumer passed. See
  `230-vnext-http-flowcontent-results.md`.
- `FluxFlow.Components.FileSystem` is now `4.0.0` locally with canonical
  exact-byte reads/writes, FlowContent metadata, stable normal result kinds and
  errors, strong lineage, and FlowValue directory/watch sources. Expected
  operation failures remain ordinary data; source infrastructure failures are
  isolated as Completion faults. Released typed nodes remain available
  unchanged. FileSystem Composition is now `2.0.0` with canonical fixed ports,
  no universal Errors surfaces, explicit typed compatibility registrations,
  and updated Designer metadata. Focused/full tests, controlled builds, binary
  compatibility, preflight/dry-runs, and a package-only consumer passed. See
  `231-vnext-filesystem-flowcontent-results.md`; the Storage follow-on is
  recorded in `232-vnext-storage-flowcontent-results.md`.
- `FluxFlow.Components.Storage` is now `4.0.0` locally with canonical exact
  FlowContent records, stable normal result kinds/errors, strong lineage, and a
  private versioned JSON-compatible envelope over the released store value
  boundary. Expected operation failures remain ordinary data, while released
  typed nodes and host-owned store/factory contracts remain available
  unchanged. Storage Composition is now `2.0.0` with canonical one-output
  fixed ports, no universal Errors surfaces, explicit typed compatibility
  registrations, and updated Designer metadata. FileSystem and SqlFile adapter
  integration, focused/full tests, controlled builds, binary compatibility,
  preflight/dry-runs, and a package-only consumer passed. See
  `232-vnext-storage-flowcontent-results.md`.
- `FluxFlow.Components.Sessions` is now `4.0.0` locally with canonical exact
  FlowContent recorder/replay contracts, stable normal result kinds/errors,
  strong lineage, and a private versioned JSON-compatible envelope over the
  released object payload boundary. Missing sessions, malformed records,
  validation failures, and store failures remain ordinary data; released typed
  nodes and host-owned store/factory contracts remain available. Sessions
  Composition is now `2.0.0` with canonical fixed one-output ports, no
  universal Errors surface, explicit typed compatibility registrations, and
  updated Designer metadata. Focused/full tests, controlled builds,
  zero-warning affected rebuilds, binary compatibility, preflight/dry-runs,
  and a package-only consumer passed. See
  `233-vnext-sessions-flowcontent-results.md`.
- `FluxFlow.Components.Resources`, `FluxFlow.Components.Secrets`, and
  `FluxFlow.Components.Configuration` are now `2.0.0` locally. Resource and
  secret names use canonical nested `Resources...` application addresses,
  descriptors require explicit host/resource-revision/external ownership, and
  keyed DI APIs distinguish provider-owned factories from non-owning external
  bridges. Configuration builders and diagnostics use the same contracts.
  Focused/full tests, controlled builds, release preflight, package dry-runs,
  and a package-only ownership consumer passed. Package validation against the
  preceding 1.x packages reported only the intentional major-version API
  removals. See `234-vnext-resource-address-ownership.md`.
- `FluxFlow.Composition.Hosting` is now `2.2.0` locally with canonical static
  and configuration definition sources plus a DI-backed application revision
  host. Initial source failures are degraded `FlowError` results rather than
  .NET host failures; rejected reloads preserve an active revision; hosted stop
  drains and disposes exactly once. Candidate construction and revision events
  remain explicit, and Hosting remains Engine-independent. The released
  standalone `CompositionDefinition` host remains available as compatibility.
  Focused/full tests, controlled builds, additive binary validation, preflight,
  dry-run, and a package-only hosted revision consumer passed. See
  `235-vnext-canonical-application-hosting.md`.
- `FluxFlow.Components.Designer` is now `2.19.0` locally with canonical flat
  application persistence over Composition JSON, addresses, and link
  diagnostics. Editable models preserve loaded declaration side, conditions,
  nested resources, resource references, malformed raw declarations, and
  unrendered application content; new workflow links default to source-side
  declarations. The sample host's parallel graph schema was removed, and the
  host plus real Designer application now distinguish signal inputs from typed
  message inputs. Focused/full tests, controlled builds, additive binary
  validation, preflight/dry-run, and a package-only persistence consumer
  passed. See `236-vnext-designer-canonical-persistence.md`.
- The complete 58-package current version set was packed into one fresh local
  source after a zero-warning controlled Release build. A fresh package-only
  `net8.0` consumer restored all 58 direct references from that source, built
  with warnings as errors, and executed successfully. Package metadata
  inspection confirmed every FluxFlow archive came from the fresh source. This
  closes local vNext implementation and coordinated package validation; it does
  not publish or tag packages. See
  `237-vnext-coordinated-package-validation.md`.
- Current architecture direction: standalone nodes are the default,
  `FluxFlow.Composition` is the optional standalone composition layer, component
  `.Composition` packages own factory registration and optional Designer
  metadata, and `FluxFlow.Engine` remains optional advanced runtime
  infrastructure.
- Eight repository review findings are fixed across Composition, Engine, Nodes,
  FileSystem, Timers, Routing, and HTTP. The affected packages are prepared as
  patch releases (Nodes `1.2.1`, Engine `2.0.3`, Composition `1.2.1`,
  FileSystem `3.1.3`, Timers `3.1.3`, Routing `3.0.3`, HTTP `3.0.3`). Focused
  suites, release tests, controlled Debug/Release builds, binary compatibility,
  release preflight, and local-source package dry-runs passed. Public API
  baselines and composition adapter versions remain unchanged. See
  `204-runtime-and-component-review-fixes.md`.
- Composition adapters now exist for the normal standalone component families:
  HTTP, Mapping, Control, Assertions, Validation, Timers, Sources, Routing,
  Serialization, Payloads, Observability, Projections, Metrics, Expectations,
  FileSystem, State, Storage, Sessions, and MQTT. Request/reply is intentionally
  skipped as a normal component-family adapter; Journal remains support-only.
- Designer has been decoupled from engine identifiers and now owns its own
  design-time value types. Package-owned metadata providers are in place across
  composition packages, with shared metadata helpers and stronger validation.
- The active narrow track is richer Designer metadata hints. Mapping was the
  pilot; Control, Assertions, State, Observability, Validation, Routing,
  Timers, Sources, Serialization, Payloads, Projections, Metrics,
  Expectations, HTTP, FileSystem, Storage, Sessions, and MQTT followed. Release
  convention tests now guard option section/importance hints, contract-valued
  editor/syntax hints, same-node related resources, and host-owned resource key
  patterns. Any further package-family or convention metadata hint work should
  be planned separately.
- Release-readiness preflight and fast dry-runs passed for the impacted
  Designer metadata hint package set after seeding a complete current-branch
  temp package source outside the repo. Publication sequencing is recorded in
  `176-designer-metadata-hint-publication-sequencing.md`, the final no-publish
  rehearsal is recorded in
  `177-designer-metadata-hint-final-release-rehearsal.md`, local tag execution
  is recorded in `178-designer-metadata-hint-local-tag-execution.md`, and tag
  push is recorded in `179-designer-metadata-hint-tag-push.md`. The release
  workflow recovery fixed the Linux release-test path normalization issue,
  retargeted the 42 dependency-ordered tags from
  `d7da08e5bad380e243cdd49988808285292d66de` to
  `31800f5b3ecb0a5985e2eb7d32be6dd2d6221f77`, and verified every release
  workflow, release asset set, and public package-feed version. The two
  already-present runtime dependency tags remained skipped. See
  `180-designer-metadata-hint-release-workflow-recovery.md`. The published
  Designer metadata hint release train plus the current MQTT adapter releases
  were then consumer-validated from the public package feed: all 44
  package-feed checks passed and a temporary `net8.0` consumer project with all
  44 direct package references restored and built successfully. See
  `182-public-package-consumer-validation.md`.
- See `155-composition-and-designer-progress.md` for the current summary and
  verification notes.
- Package README clarity was completed across all 55 manifest packages after
  the release and consumer-validation work. Runtime, composition, adapter, and
  support-package READMEs now state host/resource ownership boundaries more
  clearly where needed; no source APIs, runtime behavior, package versions,
  release notes, changelog entries, public API baselines, tags, publishing
  workflow, or release scripts changed. See
  `183-package-readme-clarity-pass.md`.
- Package binary compatibility readiness tooling now exists as
  `eng/package-binary-compat-preflight.ps1`. It validates a built package
  against a published baseline package through .NET SDK package validation.
  `components-designer` `2.16.0` passed the helper end to end, then the
  baseline feed-alignment recovery fixed the Linux release-test fixture newline
  issue, retargeted `components-http-aspnetcore-v1.0.4`, and published the nine
  current manifest package versions that were missing from the public feed:
  `FluxFlow.Components.Http.AspNetCore` `1.0.4`, `FluxFlow.Engine` `2.0.1`,
  `FluxFlow.Components.Expressions` `2.1.2`,
  `FluxFlow.Components.Resources` `1.6.0`,
  `FluxFlow.Components.Secrets` `1.6.0`,
  `FluxFlow.Components.Configuration` `1.5.0`,
  `FluxFlow.Components.Journal` `2.3.5`,
  `FluxFlow.Components.Storage.FileSystem` `3.3.4`, and
  `FluxFlow.Components.Storage.SqlFile` `3.3.4`. All nine release workflows
  completed successfully with release assets and public feed verification, and
  all 55 manifest packages passed same-version binary compatibility preflight
  against their published baselines. See
  `184-package-binary-compat-readiness.md`,
  `185-package-binary-compat-baseline-feed-alignment-blocker.md`, and
  `186-package-binary-compat-feed-alignment-recovery.md`.
- Full public package consumer validation passed for the current manifest set:
  all 55 package-feed checks passed, and a temporary `net8.0` consumer project
  outside the repository with all 55 direct package references restored from the
  public package feed and built successfully. See
  `187-full-public-package-consumer-validation.md`.
- `FluxFlow.Components.Designer` now includes neutral resource picker hint
  contracts in `2.17.0`. `ComponentResourcePickerHints.Create(...)` reads
  existing host-owned resource attributes from component metadata or a validated
  catalog and returns ordered `ComponentResourcePickerHint` values for hosts.
  It does not render UI, enumerate resource instances, resolve keyed services,
  or own resource lifetimes. See
  `188-designer-resource-picker-hint-contracts.md`.
- `FluxFlow.Components.Designer` `2.17.0` is published from
  `738f2e1cf38aaff083e6534004a7baa342020904` with tag
  `components-designer-v2.17.0`. Release workflow run `28622249640` passed,
  release assets exist, and public package-feed verification passed. See
  `189-designer-resource-picker-hint-package-release.md`.
- Full public package consumer validation passed after Designer `2.17.0`: all
  55 package-feed checks passed, and a temporary `net8.0` consumer project
  outside the repository with all 55 direct package references restored from the
  public package feed and built successfully. See
  `190-full-public-package-consumer-validation-after-designer-2-17.md`.
- Keyed resource resolution now lives as `CompositionNodeFactoryContext`
  instance methods in `FluxFlow.Composition` (`1.1.0`); the
  `FluxFlow.Composition.Hosting` context extensions are obsolete delegating
  wrappers (`1.1.0`), and all 19 `.Composition` adapter packages no longer
  reference `FluxFlow.Composition.Hosting`. `FluxFlow.Nodes` (`1.2.0`) gained
  `FlowNodeOptions.Clock` for deterministic safety-net error timestamps. See
  `192-composition-resource-helper-relocation.md`.
- A shared "Fanout" NuGet package icon is wired repo-wide via
  `Directory.Build.targets` (`assets/icon.svg` source, `assets/icon.png`
  256x256 raster); every package with an explicit `PackageId` picks it up at
  its next release. **All 55 current manifest packages now carry the icon**:
  the composition hygiene release set (`FluxFlow.Nodes` `1.2.0`,
  `FluxFlow.Composition` `1.1.0`, `FluxFlow.Composition.Hosting` `1.1.0`, 19
  `FluxFlow.Components.*.Composition` adapters) published first, then the
  remaining 33 packages (Designer `2.17.1`, core component packages, `Mapping`
  `1.0.3`, `Engine` `2.0.2`, the two MQTT adapters) were patch-bumped
  icon-only and published across 3 dependency waves (17/14/2). All 55 are
  independently verified live on the public NuGet feed (flat-container
  listing, embedded icon endpoint returns `200`, and a full temporary consumer
  project referencing all 55 packages restored and built cleanly). A
  pre-existing flaky test
  (`FlowMultiOutputAndSourceTests.Source_EmitAsync_WaitsWhenBoundedOutputIsFull`
  in `FluxFlow.Nodes.Tests`, unrelated to any session change) hit ~13% of
  release CI runs; the user approved standing auto-retry for that exact
  signature, and all affected releases passed on retry. That test has since
  been made deterministic (rewritten to the actual latest-wins delivery
  contract as
  `Source_EmitAsync_DeliversLatestThroughBoundedOutputAndCompletes`, 60/60
  isolated passes, test-only change). See
  `197-bounded-source-flaky-test-fix.md`. Syncing `origin/main`
  itself remains a pending operator step: PR #54
  (`https://github.com/araxis/FluxFlow/pull/54`,
  `work/designer-host-model` -> `main`) is open and is a clean fast-forward
  (516 commits, no divergence), but merging it into the default branch is
  gated as a human-review action and was not auto-merged. Only the
  `work/designer-host-model` branch carrying the release commits was pushed.
  See `195-nuget-icon-and-hygiene-release-prep.md` and
  `196-full-icon-rollout-completion.md`.
- The Designer host layer is planned in `docs/18-designer-host-layer.md` and
  phases 1, 2, and 4 are now implemented as the headless host-model layer in
  `samples/FluxFlow.DesignerHost` (palette, inspector, option editor, and
  resource picker view models projected by `DesignerHostCatalog`; graph model
  with lossless `GraphDefinitionMapper` round-trips to composition
  definitions; shared validation message mapping; 29 focused tests). Phase 5
  (the renderer UI) is now started as `samples/FluxFlow.DesignerApp`, a Blazor
  WebAssembly + MudBlazor app (net10.0); it was merged to `main` via PR #55.
  The docs/18 phase 5 renderer is complete and browser-verified against the
  real metadata catalog: the palette (23 components, grouped), the
  option/resource inspector, the Z.Blazor.Diagrams `3.0.4.1` node canvas
  (add-from-palette, node rendering with ports, canvas-selection-drives-
  inspector) with editable option values that write into the node configuration,
  and save/load persistence of the canvas graph to a `FluxFlow.Composition`
  definition (named-port link mapping) with validation feedback — the saved
  composition JSON carries real option values that round-trip on load. So all
  Designer host layer phases (1, 2, 4, 5) are now implemented. The canvas also
  supports delete-selected (node/link) and link-creation validation (reject
  self-links and non-output→input) on branch `work/designer-editor-polish`
  (record 202). Remaining: phase 3 (host resource-catalog adapter, needs a real
  host app) and optional undo/redo/duplicate polish. See
  `191-designer-host-layer-planning.md`,
  `193-designer-host-model-layer.md`,
  `194-designer-host-persistence-mapping.md`,
  `198-designer-renderer-app-first-slice.md`,
  `199-designer-renderer-canvas-slice.md`, and
  `200-designer-renderer-persistence-slice.md`.
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
- MQTT pilot release set is published and indexed on the public package feed:
  `FluxFlow.Components.RequestReply` `1.1.0`,
  `FluxFlow.Components.Mqtt` `4.0.0`,
  `FluxFlow.Components.Mqtt.MqttNet` `1.0.0`, and
  `FluxFlow.Components.Mqtt.PulseMqtt` `1.0.0`. PR #24 merged with squash
  commit `118a06de613a9ebdfd47e9e06b7c6761161a4d37`; release workflow runs
  `27877804072`, `27877844606`, `27877876917`, and `27877966707` completed
  successfully. The package feed was explicitly verified after publication.
  Current source keeps core `FluxFlow.Components.Mqtt` pure at `4.0.0` with no
  client capability descriptor or cross-adapter registration package.
  `FluxFlow.Components.Mqtt.MqttNet` `1.1.7` and
  `FluxFlow.Components.Mqtt.PulseMqtt` `2.0.7` are now published and indexed
  for the adapter-local DI registration, hosted lifecycle, Pulse MQTT `2.5.0`
  lifecycle, manual acknowledgement, and registration-name hardening work. See
  `181-mqtt-adapter-package-release.md`. These adapter versions were also
  included in the public package consumer validation recorded in
  `182-public-package-consumer-validation.md`.
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
