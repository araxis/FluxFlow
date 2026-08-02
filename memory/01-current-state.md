# Current State

Updated 2026-08-02 after release-verification and operations-sample cleanup.

## Canonical Boundary

- Application JSON has one root shape: `Resources` and `Workflows`.
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
- Durable input `1.1.0` preserves `EngineAccepted` as the lightweight default.
  Its explicit `WorkflowCompleted` mode requires exactly one host-owned
  `IDurableInputCompletionSource` and one provider-owned
  `IDurableInputLeaseRenewalStore`, dispatches one entry at a time, subscribes
  before Engine acceptance, and settles only an explicit exact-lease completion
  result. It never infers completion from graph shape, outputs, traces, or time.
- `FluxFlow.Engine.DurableInput.SqlFile` is the local single-machine SQLite
  provider. Its lazy transactional schema is version 2 and migrates v1 files
  without losing envelopes or operational states.
- SQL-file durable input `1.1.0` exposes the renewal capability through the
  same singleton. Renewal updates only the expiry of the exact unexpired leased
  token to the exact requested value and requires no schema migration.
- `FluxFlow.Engine.DurableInput.TSql` 1.0.0 is the separate opt-in shared
  network provider. One flat callback produces immutable redacted options; one
  singleton implements store, dead-letter, and renewal capabilities. Merely
  installing or resolving unrelated/default services performs no database I/O.
- The input T-SQL provider uses direct parameterized commands, operation-scoped
  pooled connections, serializable idempotent enqueue, locking-read-committed
  atomic batch leases, token-and-expiry compare-and-set transitions/renewal,
  and generation-protected replay. Version-1 schema ownership is explicit
  through `CreateOrMigrate` or read-only `ValidateOnly`; partial/incompatible
  schemas and RCSI fail closed.
- Its code, fast tests, full repository gates, public API, and package archive
  are validated. The explicit real-server suite passes all 64 executions with
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
  It contains 59 maintained packages after both T-SQL durability providers
  joined DurableInput, DurableOutput, and their SQL-file providers, and after
  the two Control markers were retired.
- The solution now contains 133 projects. The current serialized Release build
  completes 134 build targets and remains acyclic and warning-free.

## Documentation And Verification

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
