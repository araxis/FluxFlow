# GOAL: Add read-only operational status for durable input and output

## Status

Accepted before implementation and completed on 2026-08-01. This file remains
the authoritative executable prompt and permanent engineering record for the
round. The accepted scope above is preserved; exact execution evidence is
appended below.

## Executive intent

Add the smallest honest operational-inspection boundary for FluxFlow durable
input and durable output. Hosts must be able to obtain immutable, payload-free
snapshots of backlog and lease state without querying provider-owned tables,
changing workflow definitions, or introducing a monitoring framework.

The capability is optional and provider-neutral. The existing SQL-file and
T-SQL providers implement it through their already container-owned singleton
stores and expose one additional DI alias. Inspection performs no writes,
creates or migrates no schema, starts no worker, and retains no caller state.

This is an operability round, not retention, checkpointing, transport, or
exactly-once work. Existing in-process and durable execution semantics remain
unchanged.

## Governing principles

- Keep KISS, SRP, IOC, explicit ownership, and small composable contracts.
- Preserve Engine's lightweight provider-free in-process path.
- Keep status outside Engine, application definitions, JSON, C# authoring DSL,
  component settings, and `FluxFlowApplicationOptions`.
- Use direct C#, immutable records, explicit DI aliases, parameterized SQL, and
  normal asynchronous cancellation.
- Add no reflection, assembly scanning, dynamic proxy, service locator,
  generic repository, ORM, hosted polling service, timer, exporter, or hidden
  retry loop.
- Add no new third-party or framework package dependency.
- Do not create a universal input/output store abstraction. Input and output
  retain separate cohesive status contracts because their state models differ.
- Do not enlarge `IDurableInputStore`, `IDurableOutputStore`,
  `IDurableOutputDeliveryStore`, or either dead-letter contract.
- Keep provider registration callbacks and immutable provider settings
  unchanged; status has no configuration of its own.
- Preserve all existing functionality. Breaking changes are allowed in this
  repository, but none are needed for this additive capability.

## Current boundary to preserve

- Durable input has one persisted row per input key with `Pending`, `Leased`,
  `Delivered`, or `DeadLettered` state.
- Durable output stores immutable captures separately from delivery state.
- SQL-file output deliberately creates the capture schema independently from
  the delivery schema. Capture-only hosts must continue to avoid delivery-table
  creation or migration.
- T-SQL output creates and validates its capture and delivery tables as one
  provider schema, but status must still remain read-only.
- SQL-file and T-SQL registrations expose all implemented capability contracts
  as aliases of one concrete singleton; status must follow that pattern.
- Provider registration and service resolution currently perform no database
  I/O and must continue to do so.

## Provider-neutral input contract

Add a focused public contract file to `FluxFlow.Engine.DurableInput` containing
the following concepts. Use these exact semantic names unless compilation or
an existing repository naming rule requires a narrowly documented adjustment.

### `DurableInputStatusQuery`

An immutable sealed record with one constructor argument and one get-only
property:

- `DateTimeOffset ObservedAt` — the caller-selected observation boundary.

The caller supplies time explicitly. Do not inject or resolve `TimeProvider`
inside a store and do not use ambient wall-clock time. Preserve the exact
`ObservedAt` value, including its offset, in the returned snapshot.

### `DurableInputStatusSnapshot`

An immutable sealed record exposing:

- `ObservedAt`;
- `PendingCount` — all pending rows;
- `ReadyPendingCount` — pending rows whose `NextAttemptAt <= ObservedAt`;
- `LeasedCount` — all leased rows;
- `ExpiredLeaseCount` — leased rows whose `LeaseUntil <= ObservedAt`;
- `DeliveredCount` — delivered idempotency tombstones;
- `DeadLetteredCount` — current dead letters;
- `OldestReadyAt` — the earliest effective due time among ready pending rows
  and expired leases, or null when nothing is ready;
- `NextLeaseExpiry` — the earliest strictly future active lease expiry, or
  null when there is no active lease; and
- `TotalCount` — a derived checked sum of the four persisted state counts.

Constructor validation must reject negative counts, ready counts greater than
their parent state counts, inconsistent nullable time signals, and undefined
state relationships. `OldestReadyAt`, when present, must be at or before
`ObservedAt`; `NextLeaseExpiry`, when present, must be after `ObservedAt`.

The snapshot contains no key, address, contract, payload, header, trace,
correlation, causation, lease owner/token, failure description, or exception.

### `IDurableInputStatusStore`

Add one method:

```csharp
ValueTask<DurableInputStatusSnapshot> GetStatusAsync(
    DurableInputStatusQuery query,
    CancellationToken cancellationToken = default);
```

Reject a null query. Honor pre-cancellation before opening a connection and
propagate cancellation during connection and command execution. Do not retain
the query or snapshot.

## Provider-neutral output contract

Add a focused public contract file to `FluxFlow.Engine.DurableOutput`.

### `DurableOutputStatusQuery`

An immutable sealed record equivalent in timing semantics to the input query,
with explicit `ObservedAt` and no ambient clock.

### `DurableOutputStatusSnapshot`

An immutable sealed record exposing:

- `ObservedAt`;
- `CapturedCount` — every durable output capture;
- `UnmaterializedCount` — captures with no delivery-state row yet;
- `ReadyUnmaterializedCount` — unmaterialized captures whose captured-at time
  is at or before `ObservedAt`;
- `PendingCount` — materialized pending delivery rows;
- `ReadyPendingCount` — pending rows due at or before `ObservedAt`;
- `LeasedCount` — materialized leased rows;
- `ExpiredLeaseCount` — leased rows expiring at or before `ObservedAt`;
- `CompletedCount` — delivery-completion tombstones;
- `DeadLetteredCount` — current output dead letters;
- `OldestReadyAt` — the earliest captured-at time of an unmaterialized output,
  pending due time, or expired lease time that is at or before `ObservedAt`;
- `NextLeaseExpiry` — the earliest strictly future active lease expiry;
- `TrackedDeliveryCount` — a derived checked sum of pending, leased,
  completed, and dead-lettered rows; and
- `ReadyCount` — a derived checked sum of ready-unmaterialized, ready-pending,
  and expired-lease counts.

Validate all counts and relationships. `ReadyUnmaterializedCount` cannot exceed
`UnmaterializedCount`. `UnmaterializedCount` plus
`TrackedDeliveryCount` must equal `CapturedCount`. Ready/expired subsets cannot
exceed parent counts. Nullable time signals must agree with ready and active
lease presence and with the observation boundary.

This snapshot is also strictly metadata-only and payload-free.

### `IDurableOutputStatusStore`

Add the output equivalent of `GetStatusAsync(...)` using
`DurableOutputStatusQuery` and `DurableOutputStatusSnapshot`.

## SQL-file durable-input implementation

- Make `SqlFileDurableInputStore` implement `IDurableInputStatusStore` in a
  focused status partial file.
- Status must not call the normal lazy initialization path because that path
  may create a directory, database, schema, or migration.
- Open an operation-scoped read-only SQLite connection. If the configured file
  does not exist, preserve an explicit deterministic missing-database failure;
  never create it during inspection.
- Apply the configured busy timeout without changing schema or data.
- Execute one parameterized aggregate status query over
  `fluxflow_durable_inputs`; do not materialize rows or payload columns.
- Count every defined state and detect undefined/corrupt state values rather
  than silently omitting them.
- Calculate readiness using UTC ticks and the inclusive `<= ObservedAt`
  boundary. Calculate active expiry using the strict `> ObservedAt` boundary.
- Translate busy/locked failures through the provider's established safe busy
  exception convention with an operation name specific to status inspection.
- Keep disposal checks and pooled-connection cleanup behavior unchanged.

## T-SQL durable-input implementation

- Make `TSqlDurableInputStore` implement `IDurableInputStatusStore` in a focused
  status partial file.
- Do not invoke schema create/migrate initialization from status. Open one
  operation-scoped pooled connection and execute one parameterized aggregate
  query against `dbo.fluxflow_relational_inputs`.
- Reuse the configured command timeout and bounded connection-open behavior.
- Do not start a transaction or request update/row locks; this is a read-only
  operational statement.
- Use the same inclusive readiness and strict active-expiry boundaries as the
  SQL-file provider.
- Detect invalid state values and return no row-level or secret data.
- Missing, partial, incompatible, or inaccessible schema must fail visibly and
  safely; inspection must not repair it.

## SQL-file durable-output implementation

- Make `SqlFileDurableOutputStore` implement `IDurableOutputStatusStore` in a
  focused status partial file.
- Open SQLite read-only and do not call capture or delivery schema
  initialization.
- Perform a small read-only catalog probe to determine whether the independent
  delivery table exists. This metadata probe is allowed and must not be
  represented as the aggregate status query.
- If the delivery table does not exist, run one aggregate query over captures
  only. Report every capture as unmaterialized, zero tracked delivery states,
  and use eligible captured-at values for `OldestReadyAt`. Do not create the
  delivery schema.
- If the delivery table exists, run one aggregate query joining captures to
  delivery state. Count unmaterialized captures and every defined delivery
  state in the same statement.
- Include unmaterialized captures in `ReadyCount` only when their captured-at
  time is at or before `ObservedAt`.
- Detect orphan delivery rows, undefined states, and impossible count
  relationships as corruption rather than hiding them.
- Read only key/state/time columns needed for aggregation; never select payload
  or diagnostic data.
- Preserve normal configured busy handling, cancellation, and disposal.

## T-SQL durable-output implementation

- Make `TSqlDurableOutputStore` implement `IDurableOutputStatusStore` in a
  focused status partial file.
- Do not invoke create/migrate initialization. Use one operation-scoped pooled
  connection, one configured-timeout aggregate statement, and no write lock or
  explicit transaction.
- Aggregate captures with a left join to delivery rows so a concurrent or
  not-yet-backfilled capture is reported as unmaterialized.
- Detect orphan delivery rows and undefined state values explicitly. If a
  second small integrity statement is strictly required to detect orphans,
  fold it into the same command/result set rather than loading records.
- Preserve the same counts, boundaries, corruption behavior, cancellation, and
  payload-free guarantees as SQL-file output.

## DI registration and ownership

For each provider registration method:

- register the new status interface as one singleton alias resolving the
  existing concrete store;
- include the status contract in conflict detection;
- include the exact alias in equivalent-registration/tamper detection;
- reject partial or externally pre-owned status registrations atomically;
- preserve equivalent repeated registration as idempotent;
- preserve different-option registration as an error;
- preserve zero database/filesystem I/O during registration and resolution;
- ensure concrete store and every existing/new interface resolve to the exact
  same object instance; and
- create no status builder, options object, callback, hosted service, or
  additional singleton.

The four providers are:

- `FluxFlow.Engine.DurableInput.SqlFile`;
- `FluxFlow.Engine.DurableInput.TSql`;
- `FluxFlow.Engine.DurableOutput.SqlFile`; and
- `FluxFlow.Engine.DurableOutput.TSql`.

## Package versions and dependencies

Treat the new public contracts and provider capabilities as additive minor
releases:

- `FluxFlow.Engine.DurableInput`: `1.1.0` -> `1.2.0`;
- `FluxFlow.Engine.DurableInput.SqlFile`: `1.1.0` -> `1.2.0`;
- `FluxFlow.Engine.DurableInput.TSql`: `1.0.0` -> `1.1.0`;
- `FluxFlow.Engine.DurableOutput`: `2.0.0` -> `2.1.0`;
- `FluxFlow.Engine.DurableOutput.SqlFile`: `2.0.0` -> `2.1.0`; and
- `FluxFlow.Engine.DurableOutput.TSql`: `1.0.0` -> `1.1.0`.

Keep `net8.0;net10.0`. Do not add or replace packages. Update internal package
dependency expectations and release evidence consistently. Keep all six
projects in their current solution/package-manifest positions.

## Test architecture

Use the existing xUnit + Shouldly stack. Invoke the repository's mandatory
test-generation workflow before test-source edits and maintain
`.testagent/research.md`, `.testagent/plan.md`, and `.testagent/status.md`.

### Contract tests

Test both query and snapshot models for:

- exact observation-time/offset preservation;
- all valid state/count/time combinations;
- checked derived totals;
- every negative count;
- ready/expired subset overflow;
- captured/tracked/unmaterialized mismatch;
- missing or unexpected readiness/expiry timestamps;
- inclusive ready and strict active-expiry boundaries; and
- public property surfaces containing no payload, key, token, owner, failure,
  exception, or header fields.

### Reusable conformance suites

Add one narrow input status context/suite and one narrow output status
context/suite. A provider supplies a fresh real store and deterministic state
setup through explicit context methods; do not expose SQL or provider details
through the shared contract.

Conformance must prove:

- empty snapshots;
- mixed states with exact counts and derived totals;
- not-due, exact-due, active, and exact-expiry boundaries;
- earliest-ready and next-active-expiry selection;
- delivered/completed and dead-letter state counting;
- output unmaterialized capture behavior;
- snapshots do not mutate state or consume/lease/replay records;
- pre-cancellation causes no mutation; and
- repeated snapshots reflect later committed transitions without cached state.

### SQL-file provider tests

Use unique real temporary SQLite files. Cover:

- inherited conformance for input and output;
- same-singleton status aliases and all registration conflict/tamper cases;
- missing database/path inspection creates no directory or file;
- output inspection creates no delivery schema when it is absent;
- existing capture-only records are reported unmaterialized;
- delivery initialization/backfill changes later snapshots correctly;
- invalid state/orphan corruption fails visibly;
- busy timeout behavior and recovery;
- reopening with a fresh store returns the same committed snapshot;
- pre-cancellation and disposal; and
- no schema-version/index/table mutation from status.

### T-SQL provider tests

Fast tests cover contracts, options/registration, singleton aliases,
pre-cancellation before connection opening, safe diagnostics, and package
boundaries without requiring a server.

Explicit SQL Server integration tests cover:

- inherited input/output status conformance;
- mixed persisted state and exact tick-boundary aggregation;
- unmaterialized output capture before backfill;
- multi-store visibility after commit;
- no schema create/migrate/repair from inspection;
- malformed/partial/missing schema failure;
- cancellation, command timeout/locking behavior, disposal, and recovery;
- state corruption/orphan detection where constraints can be deliberately
  bypassed in the isolated test database; and
- zero skipped tests through the existing disposable-container runners.

Do not add sleeps, random race timing, fake databases, or silent skips.

### Test-quality completion

- Map every acceptance requirement to exact tests before completion.
- Run assertion-quality and pseudo-mutation/gap review after tests pass.
- Strengthen tests for survived meaningful mutations; do not weaken assertions
  or change production behavior merely to satisfy a mistaken expectation.
- Record exact test names and outcomes in `.testagent/status.md` and in the
  final `Requirement | Evidence` report.

## Documentation and memory

- Add `docs/35-durability-operational-status.md` and link it from
  `docs/README.md`.
- Update root README package descriptions and all six package READMEs with
  status resolution/examples and the read-only/no-payload boundary.
- Update relevant durable-input/output documentation where provider capability
  lists would otherwise be stale.
- Add new changelog entries for all six additive minor releases.
- Add `memory/282-durability-operational-status.md` and link it from
  `memory/00-index.md`.
- Update `memory/01-current-state.md` and `memory/07-progress-log.md`.
- Append final evidence to this goal file rather than changing its accepted
  implementation requirements.
- Examples must use placeholder configuration access and no literal secret.

## Explicit non-goals

Do not add:

- automatic retention, purge, archive, or deletion;
- a background status poller, scheduler, hosted worker, timer, or cache;
- ASP.NET Core health-check registration;
- OpenTelemetry/Meter exporters or a metrics package;
- dashboards, endpoints, CLI, Designer UI, or administration service;
- workflow checkpoint/resume, durable internal node state, or revision state;
- exactly-once execution, distributed transactions, or business-state atomicity;
- transport adapters, batching/parallel dispatcher changes, or replay changes;
- new provider settings or changes to application/component definitions;
- provider discovery, reflection, generic status repositories, or a shared
  input/output god interface;
- schema versions, migrations, tables, columns, indexes, or summary rows solely
  for status; or
- additional database engines.

## Execution order

1. Preserve this accepted goal before other edits.
2. Inspect the exact current contracts, schemas, registration ownership,
   versions, tests, docs, and release/public API conventions.
3. Record the bounded test research and requirement-to-test plan.
4. Add provider-neutral immutable status contracts and contract tests.
5. Implement SQL-file input/output status with read-only connections.
6. Implement T-SQL input/output status with configured bounded commands.
7. Add exact singleton aliases and registration conflict/tamper behavior.
8. Add shared conformance and provider-specific tests.
9. Update versions, changelog, READMEs, documentation navigation, goal, and
   memory.
10. Run focused builds/tests for both target frameworks.
11. Run both explicit SQL Server integration runners with zero failures and
    zero skips; record tested image tag/digest and cleanup evidence.
12. Run Debug and Release solution builds, the serialized default Release test
    suite, release/package/public API/documentation governance, package archive
    and consumer smoke checks appropriate to six changed packages.
13. Run formatting, `git diff --check`, dependency/forbidden-pattern scans,
    assertion-quality review, and pseudo-mutation/gap review.
14. Append exact evidence and mark the goal complete only when every required
    gate has honestly passed or document a concrete external blocker.

## Completion criteria

The goal is complete only when:

- both provider-neutral status APIs are immutable, explicit, validated, and
  payload-free;
- all four providers implement the matching optional capability on their
  existing singleton;
- status performs no writes or schema changes and output capture-only
  inspection does not initialize delivery schema;
- exact state, readiness, expiry, unmaterialized, and derived-count semantics
  match across SQLite and SQL Server;
- registration is atomic, idempotent for equivalent configuration,
  tamper-aware, conflict-safe, and I/O-free;
- Engine, definitions, JSON, DSL, application/component settings, dispatcher,
  replay, and retention behavior remain unchanged;
- no prohibited dependency, magic, background work, or abstraction is added;
- focused, conformance, provider, real-server, repository, release, public API,
  packaging, formatting, assertion, and gap gates pass;
- real-server tests have zero skips and their immutable image digests are
  recorded;
- docs, package READMEs, changelog, goal, test-agent records, and memory are
  current; and
- the final report maps each requirement group to concrete tests or validation
  evidence.

## Execution evidence

### Delivered surface

- Added separate immutable `DurableInputStatusQuery`/
  `DurableInputStatusSnapshot`/`IDurableInputStatusStore` and
  `DurableOutputStatusQuery`/`DurableOutputStatusSnapshot`/
  `IDurableOutputStatusStore` contracts. All counts are validated, derived
  totals use checked arithmetic, observation time remains caller-owned, and
  snapshots contain no payload or lease-owner data.
- Added focused read-only status implementations to the existing SQL-file and
  T-SQL input/output singleton stores. The queries calculate exact ready and
  expired subsets at `ObservedAt`, preserve output capture-versus-delivery
  distinctions, and fail visibly on undefined states or orphan delivery rows.
- SQL-file inspection bypasses lazy schema initialization and uses an unpooled
  operation-scoped read-only connection. Output capture-only inspection probes
  for delivery schema without creating it and reports captures as
  unmaterialized when that schema is absent.
- T-SQL inspection uses one parameterized aggregate command through the
  provider's bounded connection-open and command-timeout paths. It opens no
  write transaction and performs no schema initialization.
- All four provider registrations expose the status contract as an exact alias
  of the existing concrete singleton. Equivalent registration, conflict,
  partial ownership, and tamper behavior remain atomic and I/O-free.
- Advanced the six additive package lines to input core/SQL-file 1.2.0, input
  T-SQL 1.1.0, output core/SQL-file 2.1.0, and output T-SQL 1.1.0. Public API,
  package documentation, changelog, site navigation, and memory were updated.
- Fresh package consumption exposed an advisory in the existing SQLite native
  bundle. The central pin was moved from 2.1.11 to the compatible patched
  2.1.12 line; no dependency was added. Both SQL-file provider dependency
  scans and repeated package consumers then reported zero vulnerability or
  build warnings.

### Requirement-to-evidence map

| Requirement group | Concrete evidence |
| --- | --- |
| Immutable, validated, payload-free provider-neutral contracts | Input/output contract tests cover constructor validation, checked totals, exact `ObservedAt`, empty snapshots, and public shape. |
| Exact input/output state, readiness, lease-expiry, unmaterialized, and derived-count semantics | Shared conformance plus SQL-file and T-SQL status suites cover every state, boundary equality, mixed state, empty store, corruption, and derived count relationship. The focused local status matrix passed 102 tests; six package-version assertions also passed. |
| Read-only, schema-free provider behavior | SQL-file tests cover missing database, missing delivery schema, capture-only state, reopened state, file cleanup, and no mutation. T-SQL integration tests cover absent schema, lock behavior, persisted state, corruption, and no status-side initialization. |
| Exact singleton registration ownership | Provider DI tests cover exact alias identity, normalized-equivalent idempotency, conflicts, partial ownership, tampering, and registration/resolution without database I/O. |
| Real SQL Server behavior | Durable-input integration passed 77/77 and durable-output integration passed 87/87, with zero failures and zero skips, against SQL Server 2022 image digest `sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`. The owned containers were removed and final `docker ps` output was empty. |
| Repository safety and regression coverage | Serialized Debug and Release solution builds traversed 133 projects with zero errors/warnings. The default serialized Release suite passed 2,358/2,358 tests across 66 projects; Release governance passed 117/117. After the SQLite patch, Release rebuilt cleanly and the complete input/output SQL-file projects passed 111/111 and 134/134 respectively. |
| Public API, packages, and external consumption | The accepted 59-package API baseline passed. All six package/symbol archives passed archive inspection, feed verification, and isolated external consumer execution on `net8.0` and `net10.0`. Binary-compatibility commands were prepared for all six previous-version coordinates; actual comparison cannot run because none of the six package families or baselines is published on the configured public feed and dry-run packaging intentionally retains only the current artifacts. |
| Simplicity and prohibited-boundary checks | Source review and static scans found no reflection, ORM, generic repository, health check, metric exporter, hosted worker, timer, hidden retry, or new configuration/schema surface. Assertion-quality, gap, and pseudo-mutation reviews recorded no unresolved status requirement. |
| Hygiene and records | Full-solution `dotnet format --verify-no-changes`, `git diff --check`, package vulnerability scans, temporary-cache cleanup, documentation, changelog, goal, test-agent records, and memory checks passed. |

### Final result

Every implementable completion criterion passed. The only unavailable
comparison is against unpublished prior package binaries; the repository's
binary-compatibility preflight is fully prepared with the intended baselines,
and the additive public API baseline plus external package consumers passed.
No Engine, workflow-definition, JSON, DSL, application/component option,
dispatcher, replay, retention, or schema behavior was changed.
