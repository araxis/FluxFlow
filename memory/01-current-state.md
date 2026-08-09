# Current State

Updated 2026-08-08 after the code-first path was completed end to end, an
optional standard .NET application-readiness adapter was added, and a bounded
performance/concurrency/lifetime hardening round established a permanent
benchmark baseline. Component and resource contracts carry executable
behavior into definitions, typed handles continue through runtime and
durability operations, Fluent uses the canonical Engine lifecycle, and dynamic
registration is explicitly advanced. Portable JSON/hot reload and package-only
process-restart durability remain independent and preserved.

Release preparation now freezes a 31-package `rc.1` closure for the breaking
code-first surface. Twenty-seven packages contain direct changes and four move
to preserve dependency floors. The plan uses four dependency-derived waves,
trusted short-lived publication credentials, and a separate public-feed-only
pilot before any stable promotion. No prerelease package has been published at
the time of this record; see `memory/306-code-first-prerelease-preparation.md`.

## Performance And Reliability Boundary

- `benchmarks/FluxFlow.Engine.Benchmarks` is a permanent non-packable .NET 10
  suite over real code-first Engine applications. Eight cases compare typed and
  addressed request/reply, conditional routing, one/eight-hop pipelines, and
  1/32/128-link compilation.
- Benchmark timing is manual, machine-local evidence. CI asserts deterministic
  behavior only; it has no elapsed-time or allocation threshold.
- Two measured internal changes preserve the public surface: unconditional
  routes skip condition-context allocation, and small runtime diagnostic maps
  avoid `params` arrays plus LINQ iterators.
- On the recorded machine, eight-hop ShortRun allocation fell from 53.25 KB to
  43.29 KB (about 18.7 percent). The final default job reports 36.95
  microseconds/7.87 KB for one hop and 192.26 microseconds/43.18 KB for eight.
- Typed and addressed requests allocate equivalently (12.99 KB versus 12.95 KB
  in the final default job); no meaningful typed-handle runtime tax is visible.
- Engine reliability tests use causal gates and exact counts for concurrent
  requests, compatible/rejected revisions, retained routes, in-flight stop,
  drain, cleanup, and retirement. There are no sleeps, polling loops,
  Stopwatch thresholds, or retry-until-green assertions in the new slice.
- Detailed scan counts, before/after measurements, environment, commands, and
  limits are recorded in
  `memory/303-performance-concurrency-lifetime-baseline.md` and
  `docs/43-performance-concurrency-lifetime-baseline.md`.
- Final proof is a warning-free 137-project CI-style Release build, 2,673/2,673
  solution tests across 67 projects, 134/134 Engine tests, 189/189 Release
  governance tests, all eight dry benchmark cases, clean formatting/whitespace,
  and no known vulnerable dependency in the new benchmark project.

## Published Package Boundary

- The 59 previously maintained package versions remain available from the
  public package feed. Fifty-eight versions were newly published from commit
  `d54f1f4a`; `FluxFlow.Mapping` 1.0.3 was the audited reused prerequisite and
  was not republished. `FluxFlow.Engine.HealthChecks` 1.0.0 is the new sixtieth
  manifest entry and has not been published in this workspace round.
- The health package's first candidate version is frozen as `1.0.0-rc.1`. The
  other 30 affected packages use next-major `rc.1` versions because the
  authoring surface or a required package dependency floor is breaking.
- Every new package tag and repository release targets the exact publication
  commit and contains the exact package plus symbol assets.
- All 59 packages restore and load through isolated public-feed-only consumers.
  Separate executable proof resolves Engine, runs a hosted Fluent graph, and
  performs real SQL-file durable-input and durable-output enqueues.
- Publication is fail-closed: exact public absence is required immediately
  before upload, duplicate skipping is forbidden, public indexing and a clean
  consumer precede repository-release creation, and dependency waves are
  calculated from explicit package-project references.
- Every manifest entry carries an explicit binary-compatibility baseline. The
  release resolver rejects missing or malformed policy, and the workflow uses
  the SDK compatibility preflight as its only package-creation path before
  archive inspection or publication. Explicit JSON `null` is reserved for a
  genuine first release and skips only the unavailable prior-package
  comparison.
- Normal CI and complete package rehearsals use one checked-in external
  package-only consumer for representative behavior. Its isolated restore
  rejects project libraries and proves every restored FluxFlow archive exactly
  matches the candidate source before executing canonical Engine, Fluent, and
  SQL-file durable-input/output reopen scenarios. The same runner then starts
  separate seed and recovery processes over one SQL-file state directory. It
  proves expired input/output leases recover through normal hosted dispatch,
  workflow output is captured and delivered, and a host-owned effect keyed by
  `DurableOutputEnvelope.Key` is not repeated. The contract remains
  at-least-once and does not persist workflow execution state. The existing
  per-package restore/load smoke remains the inventory-wide check.
- The complete execution, workflow run ids, recoveries, and cleanup evidence
  are recorded in `memory/292-coordinated-release-train.md`.
- `FluxFlow.Engine.HealthChecks` 1.0.0 is an optional leaf package. One
  idempotent `AddFluxFlowApplication()` call registers the standard check
  `fluxflow.application` with the exact tags `fluxflow` and `ready`. It reports
  a usable active revision as healthy, a rejected update with the previous
  revision still active as degraded, and missing/inactive/stopped application
  state as unhealthy. It reads existing in-memory state only and emits at most
  seven bounded, non-secret lifecycle and diagnostic identity fields; Engine
  has no reverse dependency, and no worker, polling, I/O, reflection, endpoint,
  or external dependency probing is added.

## Canonical Boundary

- Application JSON has one root shape: `Resources` and `Workflows`.
- Compiled C# and portable JSON are independent first-class authoring sources.
  `ApplicationDefinitionBuilder` builds a directly executable in-memory graph;
  it does not serialize or round-trip through JSON. Both sources converge at
  normalization, catalog validation, link compilation, revision activation,
  and runtime routing. Designer remains JSON-only.
- The C# builder keeps exactly the workflow-return and flat `out var` capture
  shapes. `ComponentContract` values centralize the component type, runtime
  factory/bindings, options application, and typed handle without reflection or
  declaration-time activation. All 19 Composition families expose 44 complete
  contracts through `<Family>Components`; retained family registration methods
  register the exact same descriptor.
- A code-first `ApplicationDefinition` owns the exact immutable descriptors
  introduced by its contracts. `services.AddFluxFlow(definition)` executes that
  application without duplicate runtime registration. JSON-loaded and low-level
  string definitions remain explicitly host-registered.
- Code-first application resource extensions capture exact
  `ApplicationResourceContract` values. Each contract owns one portable
  type/options projection, typed handle, and explicit registrar; Engine merges
  definition and host registrars per candidate by exact identity. JSON contains
  no executable contract and therefore retains explicit package registration.
- Typed component handles remain authoritative after activation:
  `ApplicationPorts`, durable input enqueue, durable output capture, and keyed
  resource helpers accept the same typed ports/resources and delegate to the
  existing canonical-address behavior.
- Engine resolves one effective catalog per candidate from host and
  definition-owned descriptors. Exact descriptor reuse deduplicates; a
  different descriptor for the same type fails before activation. Ordinary
  host DI is an explicit non-owned fallback behind revision-owned resources.
- Named handles expose component-specific inputs, outputs, signals, and
  explicit `Events`. `OutputPortHandle<T>.ConnectTo` returns the same output for
  fan-out and permits same-owner cross-workflow links. Workflow `Connect` is
  local; application `Connect` is the explicit cross-workflow scope.
- Link conditions may be portable expression strings or synchronous typed C#
  predicates. Predicates require no expression engine, skip error messages,
  isolate exceptions to one route, and are owned by the built definition and
  revision with no global delegate registry.
- Composition `6.0.0` owns canonical definitions, addressing, link compilation,
  immutable component descriptors/catalogs, and the focused
  `IApplicationResourceRegistrar` extension boundary.
- Composition exposes complete canonical link declarations through
  `ApplicationLinkCompilationResult.Declarations` and owns their serializer.
  Designer and Engine no longer consume Composition internals through production
  friend declarations.
- Engine `7.0.0` owns `AddFluxFlow(...)`, definition sources, the single
  `FluxFlowApplication` lifecycle, transactional revisions, runtime assembly,
  stable ports, diagnostics, generations, rollback, and disposal.
- `FluxFlow.Fluent` is an instance-first facade over that same definition and
  Engine lifecycle. `FlowGraph.Definition` and `FlowGraph.Application` expose
  the canonical objects; no parallel runtime or manual graph-link lifecycle
  remains. Acyclic graphs drain in deterministic topological stages.
- Dynamic/plugin-only descriptors are isolated behind
  `AddFluxFlowComponents().Advanced.AddDynamicComponent(...)`; the former
  normal-surface registration name is removed.
- Component and resource type resolution is exact. Runtime and Designer expose
  canonical identities only; obsolete aliases are rejected.
- Component configuration uses canonical option names. The counter option is
  `predicate`; the removed `expression` name is rejected.
- Expression engines and typed context factories are host-owned keyed services
  registered directly through built-in dependency injection. There is no
  package-global resolver, registry, or registration-wrapper package.
- Every active component family owns one `*ComponentDefinition` for public
  constants and uses one flat `AddComponent(...)` callback per designed
  component. The callback authors the runtime descriptor and presentation
  metadata together, finalizes metadata before changing DI, and automatically
  registers both immutable catalogs. Public declarations, metadata-only family
  factories, mutable catalog methods, and terminal catalog registration are
  gone.
- Normal component factories now return typed nodes. One selector-based
  `HasInput`, `HasSignalInput`, `HasOutput`, or named `HasEvents` declaration
  produces both immutable descriptor metadata and the activated Dataflow
  binding without reflection or factory execution during registration.
  The declarative `Has...` prefix is intentional: each selected Dataflow member
  already exists on the node, so the call describes and maps the component
  contract rather than creating another port. No port-level `Add...` aliases
  remain.
  Component event ports are explicit, `Events` is not globally reserved, and
  all 19 families preserve their established address through family-owned
  constants. `UseInstanceFactory` remains the validated low-level escape hatch;
  `ComponentNodeActivation<TNode>` carries the small additional-cleanup case.
- Nineteen active component composition packages remain. The empty Control
  runtime and composition migration markers are retired from source, solution,
  and release inventory; previously published versions remain restorable for
  migration only and have no replacement package.
- `FluxFlow.Nodes` 4.0.0 owns the retained `FluxFlow.Data` namespace. The
  standalone Data project/package and test project are removed without a
  forwarding assembly or type forwarders.

## Removed Surfaces

- The obsolete hosting compatibility package and its forwarding APIs are gone.
- Both legacy application-definition migrators and runtime legacy parsing are
  gone. Stored legacy documents require a one-time external conversion.
- Alias metadata, alias registration, normalization, and fallback lookup are
  gone from Composition, Engine, Designer, and component adapters.
- The disconnected Expressions, Resources, Secrets, Configuration, and Journal
  support packages and their tests are gone. Consumer-specific equivalents
  belong in the host or an explicit adapter.
- `IComponentDesignMetadataProvider`, `ComponentDesignMetadataModule`, 19
  family providers, and split family identity classes are gone.
- `ComponentDesignDeclaration`, `AddDesignerCatalog()`,
  `AddComponentDesignMetadataCatalog()`, catalog `Add`/`AddRange`/factory
  methods, 19 family `CreateMetadata()` shims, and redundant post-description
  builder methods are gone from the public surface.
- `ComponentDesignMetadataBuilder`, `OptionDesignMetadataFactory`, and
  `ResourceDesignMetadataFactory` are gone; their retained capabilities live in
  flat component registration or direct immutable metadata records.
- Production friend access from Composition to Designer and Engine is gone.
- The empty Control runtime and composition migration-marker projects are gone.

## Preserved Runtime Capabilities

- Canonical model serialization and validation.
- Component activation, immutable revision snapshots, transactional update and
  rollback, stable addressable ports, and deterministic ownership/disposal.
- Request/reply and bounded feedback signaling.
- Trace, causation, correlation, and timestamp propagation.
- System events, diagnostics, and semantic processing profiles.
- Exact keyed resource registration through `IApplicationResourceRegistrar`.

## Optional Durability

- Normal Engine ports remain bounded, in-process, provider-free, and unchanged.
- `FluxFlow.Engine.DurableInput` is an opt-in leased at-least-once ingress
  adapter. It keeps `IDurableInputStore` provider-neutral and does not persist
  workflow state, revisions, internal links, outputs, or external side effects.
- Durable input `1.3.0` preserves `EngineAccepted` as the lightweight default.
  Its explicit `WorkflowCompleted` mode requires exactly one host-owned
  `IDurableInputCompletionSource` and one provider-owned
  `IDurableInputLeaseRenewalStore`, dispatches one entry at a time, subscribes
  before Engine acceptance, and settles only an explicit exact-lease completion
  result. It never infers completion from graph shape, outputs, traces, or time.
- `FluxFlow.Engine.DurableInput.SqlFile` is the local single-machine SQLite
  provider. Its lazy transactional schema is version 2 and migrates v1 files
  without losing envelopes or operational states.
- SQL-file durable input `1.3.0` exposes renewal, status, and retention through
  the same singleton. Renewal updates only the expiry of the exact unexpired
  leased token to the exact requested value and requires no schema migration.
- `FluxFlow.Engine.DurableInput.TSql` 1.2.0 is the separate opt-in shared
  network provider. One flat callback produces immutable redacted options; one
  singleton implements store, dead-letter, renewal, status, and retention
  capabilities. Merely installing or resolving unrelated/default services
  performs no database I/O.
- The input T-SQL provider uses direct parameterized commands, operation-scoped
  pooled connections, serializable idempotent enqueue, locking-read-committed
  atomic batch leases, token-and-expiry compare-and-set transitions/renewal,
  and generation-protected replay. Version-1 schema ownership is explicit
  through `CreateOrMigrate` or read-only `ValidateOnly`; partial/incompatible
  schemas and RCSI fail closed.
- T-SQL lease `MaxCount` is an upper bound. `READPAST` lets competing workers
  skip row locks, so a simultaneous caller can transiently receive fewer rows
  or zero without losing work. Real-server tests prove bounded disjoint
  ownership, unique tokens, persisted state, and immediate recovery of every
  skipped row; a deterministic lock test fails if `READPAST` is removed.
- Its code, fast tests, full repository gates, public API, and package archive
  are validated. The explicit real-server suite passes all 90 executions with
  zero failures and zero skips against the recorded SQL Server 2022 image
  digest, completing the schema, persistence, locking, concurrency, replay,
  disposal, configuration, and provider-neutral runtime proof.
- Capable providers may separately expose `IDurableInputDeadLetterStore` for
  bounded privacy-safe listing, exact current-dead-letter lookup, and explicit
  generation-protected replay. The SQL-file provider exposes both interfaces
  through one container-owned singleton and one flat registration callback.
- `FluxFlow.Engine.DurableOutput` is an opt-in provider-neutral output-capture
  and delivery adapter. Explicitly selected workflow outputs use immutable
  envelopes, source-generated `JsonTypeInfo<T>` metadata, and one host-owned
  `IDurableOutputStore` before ordinary Engine dispatch. Capture-only behavior
  remains independent from delivery.
- `FluxFlow.Engine.DurableOutput.SqlFile` is the local single-machine SQLite
  provider. Its lazy version-1 capture schema stores complete envelopes in
  separate output tables, can share a file with durable input, and atomically returns
  `Enqueued`, equivalent-content `AlreadyExists`, or no-overwrite `Conflict`.
- The optional output-delivery boundary uses the separate
  `IDurableOutputDeliveryStore` and one host-owned
  `IDurableOutputDeliveryHandler`. One serial hosted dispatcher leases a single
  envelope, renews the exact current unexpired token while a long-running handler
  remains active, and completes the current token on success. Short handlers
  cause no renewal call. Handler failure retries at a fixed time by default; a
  nullable positive maximum can instead atomically dead-letter the final failed
  attempt. Ownership loss cancels and observes the handler and prevents stale
  settlement. Delivery is at-least-once and handlers own destination idempotency.
- `IDurableOutputDeadLetterStore` is a separate optional operator capability for
  bounded metadata-only keyset listing, exact full-envelope lookup, and explicit
  generation-protected one-record replay. The dispatcher does not resolve it,
  and replay never invokes the handler directly.
- The SQL-file singleton implements the capture, delivery, dead-letter, status,
  and retention capabilities. Its
  independent lazy delivery schema is version 2, transactionally migrates v1,
  backfills immutable captures, assigns exclusive expiring leases, preserves
  completion tombstones, and stores only stable dead-letter reason/time/
  generation metadata. Capture-only hosts never touch delivery tables.
- `DurableOutputEnvelope.HasSameContent(...)` and reusable capture, delivery,
  and dead-letter conformance suites define the complete provider behavioral
  floor. A provider supplies fresh explicit test contexts for the capabilities
  it implements while keeping schema, migration, locking, corruption, restart,
  registration, deployment, and lifecycle tests backend-specific. No runtime
  provider framework or public test package is involved.
- Engine owns only a two-interface typed capture seam. Unselected outputs keep
  the existing bounded path without serialization, store I/O, a second queue,
  or a hosted service. Configured capture preserves serial ordering and uses
  the existing output capacity for backpressure.
- Durable input and output delivery are at-least-once. Input settles when Engine
  accepts the message by default or when the exact host completion subscription
  reports success in the explicit workflow-completion mode. Output settles when
  the host handler returns and its completion token commits. Exactly-once
  execution, durable internal workflow state, producer/business-state
  atomicity, distributed transactions, and checkpoints are not claimed.
- Existing `ReceiveAsync` and `ObserveAsync` output taps remain live taps, not
  persistence contracts. Output delivery has no transport adapter, variable
  backoff, batching, parallelism, automatic/bulk replay or purge,
  administration endpoint/UI, or distributed coordination.
- `FluxFlow.Engine.DurableOutput.TSql` is the supported opt-in networked
  SQL provider. One flat builder callback produces immutable options and one
  singleton implementing the five output-store capabilities. Registration and
  resolution are atomic, tamper-aware, idempotent for normalized-equivalent
  settings, and perform no database I/O.
- The provider uses direct parameterized `Microsoft.Data.SqlClient` commands,
  operation-scoped pooled connections, bounded connection-open retry,
  configured command/schema-lock timeouts, and explicit `CreateOrMigrate` or
  `ValidateOnly` schema modes. Exact version-1 schema validation and
  transaction-owned locking fail closed; RCSI is explicitly unsupported for
  the `READPAST` leasing protocol.
- The provider preserves Engine, C# DSL, JSON, dispatcher, and application
  options. Its default fast suite passed 118 executions across `net8.0` and
  `net10.0`; the explicit real-server suite passed 73/73 with zero skips. The
  superseded executable spike is retired while its historical evidence remains.
- Durable input and output now expose separate optional payload-free operational
  status capabilities. Callers provide an explicit observation time and receive
  immutable counts for ready, leased, terminal, dead-letter, and—in the output
  model—unmaterialized capture state, plus oldest-ready and next-expiry signals.
- SQL-file and T-SQL providers expose status as one more alias of their existing
  singleton. Inspection skips normal schema initialization, performs aggregate
  reads only, creates no worker or configuration, and fails visibly on missing,
  partial, corrupt, or orphaned provider state instead of repairing it.
- Durable input and output now publish package-local BCL activities, bounded
  semantic counters, and millisecond duration histograms at capture, leased
  dispatch, renewal, handler, settlement, ownership-loss, and store-failure
  boundaries. Listener failure is isolated from processing; metric dimensions
  contain no application/message/lease identity, payload, provider, path,
  connection, exception, or secret data.
- Live instrumentation performs no status query and adds no exporter, health
  check, configuration, worker, cache, schema, provider command, or dependency.
  Explicit status snapshots remain the exact backlog view; transition metrics
  remain event/rate signals. Ordinary non-durable Engine ports are unchanged.
- `FluxFlow.DurabilityOperationsSample` demonstrates the complete host boundary
  without changing runtime code: one durable string enters a workflow, its
  transformed output is captured and delivered, and host-owned BCL listeners
  observe the semantic transitions. The sample uses source-generated JSON and
  temporary local SQL-file providers; it requires no server or credentials.
- The operations sample requests input status once before startup and again
  after delivery, plus one final output snapshot. Completion is causal and
  bounded; status is never polled. Listener callbacks perform no I/O and render
  no payload, identity, exception, path, provider, or connection data.
- The sample listener now reduces all required metric/activity keys into one
  bounded observation map and exposes one completion signal. Release tests that
  launch child processes share one xUnit collection and therefore serialize
  with each other; unrelated release tests remain parallelizable. Process
  semantics, timeouts, output, production runtime, and public contracts are
  unchanged.

## Package Lines

- Composition `6.0.0`, Engine `7.0.0`, Designer `5.0.0`, and Observability
  runtime `7.0.0` carry direct breaking surface changes.
- Storage `7.0.0` exposes immutable read-only attribute snapshots; FileSystem
  Storage and SQL-file Storage `5.0.0` consume that boundary without redundant
  provider attribute copies.
- Nodes advances once from `3.0.1` to `4.0.0` for the Data defining-assembly
  move. Data is removed rather than version-bumped.
- Composition adapters move to their next major line because their packed
  dependency closure now includes Composition `6.0.0`.
- Fluent and Fluent Hosting move to `4.0.0` for the same dependency boundary.
- DurableInput and DurableInput.SqlFile are `1.3.0`; DurableInput.TSql is
  `1.2.0`. Their additive minor versions expose explicit bounded terminal
  retention through separate aliases without changing existing stores,
  settings, schemas, or dependencies.
- DurableOutput `3.0.0` adds immutable exact-token renewal to the cohesive
  delivery interface and requires a flat positive renewal interval shorter than
  the lease duration. This is an intentional breaking change for custom
  delivery providers and direct options construction.
- DurableOutput.SqlFile `3.0.0` and DurableOutput.TSql `2.0.0` implement renewal
  through direct transactional/provider SQL without changing schema versions,
  transport, resilience, or package dependencies.
- The preceding major reset affected 51 retained packages before the Control
  markers were retired. This bounded closeout changes 20 retained packages
  (Designer plus 19 composition adapters), removes the two unmaintained marker
  entries, and advances no package version.
- `eng/packages.json` is authoritative for the complete retained inventory.
  It contains 60 maintained packages after the optional health-check adapter
  was appended as an unpublished initial release.
- The solution now restores and builds 136 projects and remains acyclic and
  warning-free.

## Documentation And Verification

- The optional application-readiness round adds one leaf package and one public
  registration method without changing Engine behavior. Final evidence is a
  warning-free 136-project Release build, 2,665/2,665 solution tests across 67
  test projects, 185/185 Release tests, an accepted and normally verified
  public API baseline, a real ten-package/fifteen-process isolated consumer,
  clean format/diff checks, and no vulnerable packages. The package has not
  been published. Detailed decisions and evidence are in
  `memory/302-application-health-readiness.md`.
- The unified complete-contract round removes duplicate runtime registration
  from normal code-first applications. Final evidence is a warning-free
  134-project Release build, 2,597/2,597 solution tests across 66 projects,
  174/174 Release tests, real candidate-package acceptance, four executable
  samples, clean format/diff/public-API/source-policy gates, and no vulnerable
  packages. Detailed decisions and evidence are in
  `memory/300-unified-code-first-component-contracts.md`.
- The declarative component-port naming refinement exposes only `HasInput`,
  `HasSignalInput`, `HasOutput`, and `HasEvents` from normal and advanced
  runtime/Designer binding builders. It intentionally retains no public
  port-level `Add...` aliases. Final evidence is a warning-free 134-project
  Release build, 2,563/2,563 solution tests across 66 projects, a real
  nine-package isolated consumer/restart pass, accepted and normally verified
  public API baseline, clean formatting/diff hygiene, and no vulnerable
  packages. Detailed rationale and evidence are recorded in
  `memory/298-declarative-component-port-naming.md`.
- The typed component-binding round replaces duplicated descriptor/runtime port
  authoring with one flat selector declaration, makes component event outputs
  explicit and named, and preserves all 19 families/44 declarations. Its final
  evidence is a warning-free 134-project Release build, 2,561/2,561 solution
  tests across 66 projects, 169/169 Release tests, a real nine-package isolated
  consumer/restart pass, clean formatting/API/policy/hygiene gates, and no
  vulnerable packages. Detailed evidence is recorded in
  `memory/297-typed-component-port-binding.md`.
- The package-only process-restart durability round changes the external
  fixture, its existing runner, focused release-governance tests,
  documentation, goals, and memory only. Runtime assemblies, public APIs,
  schemas, package versions, and publication state are unchanged. Detailed
  evidence is recorded in
  `memory/296-package-consumer-restart-durability-acceptance.md`.
- The package-consumer acceptance round changes release/CI tooling, a
  checked-in external fixture, release governance tests, documentation, goals,
  and memory only. It changes no runtime assembly, API, schema, dependency,
  package version, changelog, tag, release, or published artifact. Detailed
  evidence is recorded in `memory/295-package-consumer-acceptance-gate.md`.
- The binary-compatibility release-gate round changes release policy, tooling,
  workflow, focused governance tests, documentation, and memory only. It does
  not change runtime assemblies, schemas, dependencies, public APIs, package
  versions, tags, releases, or published artifacts. Detailed evidence is
  recorded in `memory/294-binary-compatibility-release-gate.md`.
- The two release-time output timing tests now register their non-replaying
  receiver before publication. They assert exact event/fault content and the
  request/reply in-flight transition instead of relying on scheduler order.
- The concurrency reliability round changes tests and documentation only. It
  does not change production assemblies, schema, dependencies, public APIs,
  package versions, tags, releases, or already-published artifacts. Detailed
  evidence is recorded in `memory/293-concurrency-reliability-hardening.md`.
- Retry timing verification now uses observable attempt gates and advances only
  the configured fake-time delay. Release-script and sample child processes are
  bounded, drain both redirected streams concurrently, and terminate their
  owned process tree on timeout or cancellation.
- Sample smoke tests execute the matching prebuilt Debug or Release artifacts
  with `--no-build --no-restore`; test execution cannot hide missing
  preparation behind an implicit build.
- The completed gate includes a warning-free serialized Release build across
  134 project targets, 125/125 Release tests, and two consecutive warning-free
  full Release passes of 2,488/2,488 tests across 66 projects. The durability
  instrumentation slices additionally passed 10 input and 17 output focused
  executions, all four provider-fast suites, four format gates, and fresh core
  package/symbol archive inspection.

- `docs/21-component-type-names.md` is the obsolete-to-canonical name map.
- `docs/23-engine-2-to-3-migration.md` is now the consolidated major-reset
  migration guide despite its historical filename.
- `eng/canonical-vnext-cleanup-ledger.json` records final dispositions.
- `memory/267-major-surface-reset.md` records implementation and verification
  evidence for this round.
- `memory/268-surface-simplification.md` records this continuation's declaration,
  package-boundary, link-ownership, and version decisions.
- `memory/269-declaration-closeout-and-control-retirement.md` records the final
  declaration closeout, marker-retirement decision, and verification evidence.
- `memory/270-designed-registration-and-immutable-catalog.md` records the flat
  automatic registration, immutable catalog, removed shims, and verification
  evidence for this continuation.
- `memory/271-canonical-authoring-storage-immutability-and-hot-path-cleanup.md`
  records the final authoring-path removal, immutable storage attributes,
  repeatable-path allocation cleanup, explicit MQTT mapping, and verification.
- `memory/272-durable-input-dead-letter-operations.md` records the optional
  durability boundary, SQL-file schema v2, operational replay semantics,
  verification evidence, and the next output-capture recommendation.
- `memory/273-durable-output-capture-foundation.md` records the optional typed
  capture seam, immutable output/store contracts, guarantee limits, focused and
  full verification, documentation, and the next SQL-file provider round.
- `memory/274-sql-file-durable-output-provider.md` records semantic content
  equality, reusable provider conformance, SQL-file schema/enqueue guarantees,
  extension guidance, verification, and the next delivery-contract round.
- `memory/275-durable-output-delivery-foundation.md` records the separate
  delivery contracts, serial hosted dispatcher, lazy SQL-file delivery schema,
  at-least-once guarantee, explicit limits, and complete verification evidence.
- `memory/276-durable-output-dead-letter-operations.md` records bounded-attempt
  configuration, dead-letter/replay contracts, SQL-file schema v2 and migration,
  operational privacy boundaries, verification, and the next provider step.
- `memory/277-durable-output-provider-conformance-suite.md` records the reusable
  delivery/dead-letter behavioral floor, explicit test contexts, SQL-specific
  infrastructure ownership, and complete focused/repository verification.
- `memory/278-networked-relational-durable-output-feasibility.md` records the
  direct-SQL networked provider design, real-server evidence, explicit
  limitations, and bounded production-promotion recommendation.
- `memory/282-durability-operational-status.md` records the provider-neutral
  status contracts, all four provider implementations, read-only/schema-free
  behavior, package lines, validation evidence, and deliberate limits.
- `memory/283-durable-terminal-retention.md` records the separate explicit
  retention contracts, all four provider implementations, destructive
  deduplication/replay consequences, package lines, and validation evidence.
- `memory/284-durable-output-lease-renewal.md` records the cohesive renewal
  contract, flat timing configuration, serial dispatcher race/cancellation
  rules, direct provider transitions, package lines, and validation evidence.
- `memory/285-release-test-determinism.md` records the causal retry test,
  bounded process boundary, prebuilt sample contract, independent test review,
  and complete verification evidence.
- `memory/286-durability-instrumentation.md` records exact input/output BCL
  signal names, semantic recording boundaries, cardinality/privacy limits,
  listener isolation, status separation, and complete verification evidence.
- `memory/287-durability-operations-sample.md` records the runnable host-owned
  operations scenario, exact output, documentation, and full verification.
- `memory/288-release-verification-and-sample-cleanup.md` records targeted
  process-test scheduling, fixture ownership, one-signal telemetry cleanup,
  retained test strength, and repeated normal/serialized verification.
- `memory/289-repository-release-readiness.md` records the accumulated-work
  audit and local commit boundary, release-only real-provider enforcement,
  line-ending-neutral exact sample assertion, clean detached-worktree proof,
  package audit, and owned-container cleanup.
- `memory/290-pr-65-final-review.md` records the final pull-request audit, two
  corrected P1 findings, complete local gates, remediation-head remote CI, and
  the no-merge/no-release readiness boundary.
- `memory/291-pr-65-merge-and-post-merge-validation.md` records the exact
  reviewed-head merge, post-merge stabilization, complete package rehearsal,
  and explicit no-publication boundary.
- `memory/292-coordinated-release-train.md` records the release-safety merge,
  dependency-wave execution, all 58 workflow run ids, isolated recoveries,
  59-package public proof, executable samples, and cleanup.

## Pull Request 65 Final Review

- Exceptional start/reload/apply exits restore the exact prior stable
  application state; active revision objects and last successful update remain
  unchanged.
- Durable input requires exactly one store for both enqueue and hosted
  dispatch. Ambiguous ownership fails explicitly through one non-reflective
  factory, without a public API, schema, version, or dependency change.
- The final local gate is 2,495/2,495 Release tests across 66 projects, a
  warning-free 134-target CI-style build, 127/127 release-governance tests, and
  a clean vulnerability/public-API/whitespace result.

## Pull Request 65 Merged And Validated

- Pull request 65 merged at its exact reviewed head through a normal merge
  commit. The merge parents and tree were verified; self-approval was rejected
  by the hosting service and no bypass was used.
- Two post-merge validation defects were corrected through normal pull
  requests: causal synchronization for a load-sensitive source-completion test
  and an isolated package cache for consumer smoke validation.
- Final `main` commit `ceedc36f` passes the 134-project serialized Release
  build, 2,495/2,495 solution tests, 127/127 release-governance tests,
  full-solution formatting, and the dependency vulnerability gate.
- Real providers passed 89 durable-input and 117 durable-output cases with zero
  skips. All 59 packages passed preflight, prepare-only tag resolution,
  package/symbol creation, isolated-cache consumer loading, archive inspection,
  and local-feed verification.
- Temporary worktrees, containers, package sources, archives, caches, and logs
  were removed. No tag, release, publication, or public-feed mutation occurred.
  See `memory/291-pr-65-merge-and-post-merge-validation.md`.

## Consolidated Release Candidate

- Typed C# code-first definitions now own their complete executable component
  and resource contracts. Normal hosts register the finished definition once;
  they do not repeat ordinary family registration.
- Portable JSON remains an independent data-only path with explicit package
  registration for configuration, persistence, designers, and hot reload. C#
  predicates remain intentionally code-first and are not forced into JSON.
- The exact candidate commit
  `4bf69015b9d3eaa95a45630c91d378c45c5a2aaa` passed a clean 137-project restore
  and CI-style Release build, 2,675 solution tests, 191 release tests, the
  public API baseline, formatting, whitespace, and dependency-vulnerability
  gates.
- The isolated package-only consumer packed ten exact candidate packages and
  completed 15 pack/restore/build/run invocations. It proved code-first,
  executable resources, health, Fluent, durability, restart recovery, and JSON
  rejection rollback with every required marker exactly once and full owned
  directory cleanup.
- Real T-SQL integration passed 90 durable-input and 117 durable-output cases
  with zero failures or skips. Both used the recorded pinned provider-image
  digest, and their owned containers were removed.
- No push, pull request, tag, release, or package publication occurred. See
  `memory/304-release-candidate-consolidation.md` and
  `docs/44-release-candidate-consolidation.md`.

## External Package Pilot

- A standalone application at `C:\Projects\FluxFlow.Pilot` consumes nine exact
  locally packed FluxFlow packages and contains no project reference back to
  this repository.
- Typed code-first execution uses complete contracts, typed connections and
  ports, one definition registration, standard readiness, and clean lifecycle.
- Portable JSON execution proves explicit catalog registration, unchanged
  apply, invalid-candidate rejection, active-revision retention, and successful
  post-rejection routing.
- Separate durability seed and recovery processes prove expired SQL-file input
  recovery, workflow execution, durable output capture and delivery, one exact
  effect, and terminal store state.
- All nine restore hashes matched their candidate archives; the build had zero
  warnings, all five pilot tests passed, formatting was clean, and the runner
  removed its package source, cache, and restart state.
- No production source, public API, JSON format, package version, or dependency
  changed. See `memory/305-external-package-pilot.md` and
  `docs/45-external-package-pilot.md`.
