# Goal: Add Bounded Durable-Output Failure And Dead-Letter Operations

Date: 2026-08-01
Status: accepted for execution

## Objective

Extend the optional durable-output delivery foundation with a small, explicit,
provider-neutral failure boundary:

- hosts may opt into a positive maximum delivery-attempt count;
- a handler failure on the configured final attempt moves the currently leased
  output atomically to a durable dead-letter state instead of scheduling another
  retry;
- operators may inspect current dead letters through bounded metadata-only
  pages, retrieve one exact complete envelope, and explicitly replay one current
  dead letter through a generation-protected compare-and-set transition.

The implementation must preserve FluxFlow's lightweight in-process identity.
Normal Engine outputs, unconfigured output ports, capture-only hosts, delivery
hosts that do not configure an attempt limit, workflow definitions, application
JSON, the C# DSL, components, durable input, and `FluxFlowApplicationOptions`
must retain their current behavior. This round is entirely opt-in and must add
no transport, broker, distributed coordinator, workflow checkpoint, automatic
replay, retention worker, or background work beyond the existing explicitly
registered serial durable-output dispatcher.

This file is the complete executable specification for the round. Production
work begins only after this README exists. If implementation evidence requires a
small naming adjustment, preserve every semantic requirement and record the
reason in the matching memory entry rather than silently changing scope.

## Architectural Principles

- Apply KISS, SRP, OCP, ISP, IoC, and explicit ownership pragmatically.
- Prefer direct C#, immutable records, narrow cohesive interfaces, explicit DI,
  and ordinary .NET hosting primitives.
- Keep registration flat and familiar: retain the existing one-level
  `Action<DurableOutputDeliveryOptionsBuilder>` callback and add one scalar
  builder property. Do not add nested callbacks or staged builder graphs.
- Preserve immutable runtime configuration. Mutable builders exist only during
  registration and produce one validated immutable snapshot.
- Avoid reflection, assembly scanning, convention discovery, dynamic
  activation, runtime service location, static mutable registries, generic
  repositories, provider switches, policy frameworks, and hidden fallbacks.
- Keep the dispatcher serial. Add no channel, queue, batch, fan-out,
  parallelism option, `Task.Run`, or timer abstraction.
- Use the existing `TimeProvider`, cancellation, DI, hosted-service, logging,
  and SQLite dependencies. Add no third-party dependency and no new package or
  project.
- Persist stable failure classification only. Never persist exception messages,
  stack traces, exception objects, payload excerpts, headers, arbitrary handler
  data, or destination credentials as dead-letter diagnostics.
- Preserve the current dirty worktree. Do not reset, revert, stage, commit,
  push, delete, or rewrite unrelated user changes.

## Required Package And Version Boundary

### `FluxFlow.Engine.DurableOutput`

Keep the package provider-neutral. Extend it with:

- a stable dead-letter reason enum;
- one immutable lease-owned dead-letter transition request;
- one new atomic dead-letter method on `IDurableOutputDeliveryStore`;
- immutable dead-letter query, cursor, summary, page, details, replay request,
  replay status, and replay result contracts;
- a separate optional `IDurableOutputDeadLetterStore` operational capability;
- nullable `MaxDeliveryAttempts` configuration on the existing delivery options
  and builder;
- the bounded-attempt branch in the existing serial dispatcher.

Adding a member to `IDurableOutputDeliveryStore` is a source and binary breaking
change for third-party delivery providers. Advance this package from `1.1.0` to
`2.0.0`; do not disguise the interface change as an additive minor release.
Update its project metadata, package README, changelog, package manifest,
reviewed public API baseline, and release evidence.

Do not change `IDurableOutputStore.EnqueueAsync(...)`, output capture mapping,
or `IDurableOutputDeliveryHandler.DeliverAsync(...)`.

### `FluxFlow.Engine.DurableOutput.SqlFile`

Keep one container-owned `SqlFileDurableOutputStore`. It must implement:

- `IDurableOutputStore` for capture;
- `IDurableOutputDeliveryStore` for leasing and atomic delivery-state
  transitions; and
- `IDurableOutputDeadLetterStore` for optional operator inspection and replay.

Register all three interfaces as aliases of the same concrete singleton. The
new operational alias must not initialize or access the database during service
registration. Capture-only use must still avoid delivery-schema I/O until a
delivery or dead-letter operation is actually called.

Advance this provider from `1.1.0` to `2.0.0` because its public capability and
core dependency move to the new major line. Add no transport, resilience, ORM,
or database abstraction dependency.

## Provider-Neutral Failure Contracts

All public contracts are immutable records with constructor validation. Keep
the public surface small and explicit.

### Stable reason

Add:

```csharp
public enum DurableOutputDeadLetterReason
{
    HandlerFailure = 1
}
```

`HandlerFailure` means the host-owned delivery handler threw a non-cancellation
exception and the current attempt reached the configured maximum. Undefined
enum values must be rejected by all accepting contracts and providers. Do not
persist an exception type, message, description, stack trace, retry policy, or
arbitrary reason string.

### Atomic dead-letter transition

Add an immutable `DurableOutputDeliveryDeadLetter` request containing:

- a valid `DurableOutputKey`;
- a non-empty lease token;
- `DeadLetteredAt`;
- one defined `DurableOutputDeadLetterReason`.

Extend the cohesive delivery-state interface:

```csharp
public interface IDurableOutputDeliveryStore
{
    ValueTask<DurableOutputDeliveryLease?> TryLeaseAsync(
        DurableOutputDeliveryLeaseRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DurableOutputDeliveryTransitionResult> CompleteAsync(
        DurableOutputDeliveryTransition transition,
        CancellationToken cancellationToken = default);

    ValueTask<DurableOutputDeliveryTransitionResult> RetryAsync(
        DurableOutputDeliveryRetry retry,
        CancellationToken cancellationToken = default);

    ValueTask<DurableOutputDeliveryTransitionResult> DeadLetterAsync(
        DurableOutputDeliveryDeadLetter deadLetter,
        CancellationToken cancellationToken = default);
}
```

`DeadLetterAsync` uses the existing transition result/status contract. It
applies only when the exact key currently has the exact supplied unexpired lease
token. `Applied`, `LeaseLost`, `NotFound`, and `InvalidState` retain their
current meanings. It must never overwrite a pending, completed, or already
dead-lettered row.

This interface owns the state transition because completion, retry, and
dead-lettering are mutually exclusive settlements of the same lease. Do not add
the transition to the operational inspection interface and do not make the
dispatcher depend on operator APIs.

## Bounded Operational Dead-Letter Contracts

### Query

Add immutable `DurableOutputDeadLetterQuery` with:

- optional exact `ApplicationAddress` filter;
- optional exact `DurableOutputDeadLetterReason` filter;
- optional inclusive `DeadLetteredFrom` instant;
- optional exclusive `DeadLetteredBefore` instant;
- optional `DurableOutputDeadLetterCursor`;
- positive bounded `PageSize`;
- `DefaultPageSize = 50`;
- `MaximumPageSize = 200`.

Reject undefined reasons, page sizes outside `1..200`, and a time range whose
inclusive lower bound is equal to or later than the exclusive upper bound.

### Cursor and ordering

Add immutable `DurableOutputDeadLetterCursor` containing:

- `DeadLetteredAt`;
- a valid `DurableOutputKey`.

Listing must use stable keyset pagination, never an offset. The public ordering
is:

1. `DeadLetteredAt` descending by UTC instant;
2. application address ascending with ordinal/binary comparison;
3. message id ascending with ordinal/binary comparison.

The continuation cursor identifies the last returned item. Query one extra row
to decide whether a next page exists; never return more than `PageSize`. An
empty or final page has no cursor. Equal timestamps must remain deterministic.

### Metadata-only summary and page

Add immutable `DurableOutputDeadLetterSummary` containing only:

- valid key;
- non-empty trimmed contract name;
- positive envelope schema version;
- `IsError`;
- original `CapturedAt` preserving its instant and offset;
- positive delivery attempt;
- defined dead-letter reason;
- `DeadLetteredAt` preserving its instant and offset;
- positive dead-letter generation.

The summary must not expose payload JSON, error details, error messages,
headers, traces, correlation/causation identifiers, exception data, or handler
data.

Add immutable `DurableOutputDeadLetterPage` that:

- takes an enumerable and snapshots it into an immutable collection;
- rejects null input and null items;
- exposes `IReadOnlyList<DurableOutputDeadLetterSummary> Items`;
- exposes `NextCursor` and `HasMore`;
- rejects a cursor for an empty page;
- requires a supplied cursor to identify the last returned summary exactly.

### Exact details

Add immutable `DurableOutputDeadLetterDetails` containing:

- the complete immutable `DurableOutputEnvelope`;
- positive attempt;
- defined reason;
- exact dead-letter timestamp;
- positive generation.

Exact lookup is intentionally the only operational API that returns payload,
headers, lineage, or envelope error details. A missing key or a key that is not
currently dead-lettered returns `null`.

### Explicit replay

Add immutable `DurableOutputReplay` containing:

- valid key;
- positive `ExpectedGeneration`;
- `ReplayedAt`;
- `NextAttemptAt`, not earlier than `ReplayedAt`.

Add `DurableOutputReplayStatus` with exactly:

- `Replayed`;
- `NotFound`;
- `NotDeadLettered`;
- `GenerationMismatch`.

Add immutable `DurableOutputReplayResult` with exact key, defined status, and
`IsReplayed` convenience property.

Replay is a single-record compare-and-set operation. On success it must:

- require current dead-letter state and exact expected generation;
- return the row to pending;
- set `NextAttemptAt` exactly as requested;
- reset attempt to zero so the next lease is attempt one;
- clear lease, completion, reason, and dead-letter timestamp fields;
- preserve the complete captured envelope and original capture timestamp;
- retain the generation value so a future dead-letter transition increments it
  and stale operator views cannot replay a later failure cycle.

Replay must not call the handler, start a dispatcher, choose a delay, or replay
automatically. `ReplayedAt` defines and validates the operator action boundary;
it is not a request to mutate the original captured timestamp.

### Operational capability

Add:

```csharp
public interface IDurableOutputDeadLetterStore
{
    ValueTask<DurableOutputDeadLetterPage> ListAsync(
        DurableOutputDeadLetterQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<DurableOutputDeadLetterDetails?> GetAsync(
        DurableOutputKey key,
        CancellationToken cancellationToken = default);

    ValueTask<DurableOutputReplayResult> ReplayAsync(
        DurableOutputReplay replay,
        CancellationToken cancellationToken = default);
}
```

The capability is optional for custom providers and independently resolvable by
operator/admin code. The dispatcher must not resolve it. No HTTP endpoint, UI,
CLI, authorization policy, or transport is part of this round.

## Flat Registration And Immutable Options

Preserve the existing registration shape and add one property:

```csharp
services
    .AddFluxFlowSqlFileDurableOutput(store =>
    {
        store.DatabasePath = "data/outputs.db";
    })
    .AddSingleton<IDurableOutputDeliveryHandler, OrderDeliveryHandler>()
    .AddFluxFlowDurableOutputDelivery(delivery =>
    {
        delivery.LeaseDuration = TimeSpan.FromMinutes(1);
        delivery.RetryDelay = TimeSpan.FromSeconds(10);
        delivery.IdleDelay = TimeSpan.FromMilliseconds(500);
        delivery.MaxDeliveryAttempts = 5;
    });
```

`DurableOutputDeliveryOptions` remains an immutable record. Add nullable
`int? MaxDeliveryAttempts` to its constructor and properties. Add the same
nullable property to `DurableOutputDeliveryOptionsBuilder`.

- Default is `null`.
- `null` means unlimited retry and preserves all 1.1 behavior.
- A configured value must be strictly positive.
- Zero and negative values fail before the service collection is mutated.
- Do not use `0`, a negative sentinel, `int.MaxValue`, or a hidden default to
  mean unlimited.

Existing registration guarantees remain mandatory: callback once, validation
before mutation, same-instance return, equivalent repeat idempotency, different
repeat rejection, exactly one hosted dispatcher, host clock preservation, and
no implicit handler/provider registration.

SQL-file registration must add exactly one `IDurableOutputDeadLetterStore` alias
to the existing concrete singleton. Equivalent repeats remain idempotent.
Pre-existing or tampered ownership of any of the three public store interfaces
must fail clearly before partial descriptors are appended.

## Dispatcher Behavior

Keep the current lease/handler/settlement loop and add one explicit branch after
a non-cancellation handler exception:

```text
if MaxDeliveryAttempts is null or lease.Attempt < MaxDeliveryAttempts
    retry at now + RetryDelay
else
    dead-letter at now with reason HandlerFailure
```

Required semantics:

- Attempt numbers remain one-based and are assigned by the store when leasing.
- A maximum of `1` dead-letters the first failed attempt.
- A maximum of `N` retries failed attempts `1..N-1` and dead-letters failed
  attempt `N`.
- Successful attempt `N` completes normally.
- Unlimited mode follows the existing retry path exactly and never
  dead-letters automatically.
- Host cancellation during handler or settlement leaves the lease untouched;
  expiry remains the recovery mechanism.
- The transition uses the current key, token, and `TimeProvider.GetUtcNow()`.
- A wrong result key is a store-contract failure.
- A non-applied dead-letter transition is an expected concurrency outcome; log
  metadata and continue without claiming success.
- A dead-letter store-transition exception follows the existing store-failure
  path: it is observable, the dispatcher waits `IdleDelay`, and the lease is
  left for expiry recovery.
- Logs may contain address, message id, attempt, lease owner, transition status,
  stable reason, operation, and exception type. They must not contain payloads,
  headers, `FlowError` messages/details, exception messages, stack traces as
  persisted data, or destination secrets.
- Do not add exponential backoff, jitter, policy delegates, poison-message
  classifiers, handler-result unions, or automatic replay.

## SQL-File Delivery Schema Version 2

Keep the delivery schema independent from the capture schema. Increase
`SqlFileDurableOutputDeliverySchema.CurrentVersion` from 1 to 2. Fresh delivery
initialization creates version 2 directly. Existing version-1 delivery
databases migrate transactionally and losslessly on the first delivery or
dead-letter operation.

### State and columns

Retain:

- `1 = Pending`;
- `2 = Leased`;
- `3 = Completed`.

Add:

- `4 = DeadLettered`.

Add delivery-table columns:

- nullable integer `dead_letter_reason`;
- nullable integer `dead_lettered_at_utc_ticks`;
- nullable integer `dead_lettered_at_offset_minutes`;
- non-null integer `dead_letter_generation` with default `0` and value `>= 0`.

Preserve the existing key, next-attempt, lease, attempt, completion, and foreign
key columns. Keep exact `DateTimeOffset` UTC ticks plus offset minutes where the
delivery schema already preserves offsets.

### Exact row invariants

- Pending: no lease, completion, reason, or dead-letter timestamp fields;
  attempt is non-negative; generation is non-negative.
- Leased: complete valid lease fields, positive attempt, no completion, reason,
  or dead-letter timestamp fields; generation is non-negative.
- Completed: no lease or dead-letter fields, positive attempt, complete
  delivered timestamp fields; generation is non-negative. Completed rows remain
  tombstones.
- DeadLettered: no lease or completion fields, positive attempt, one defined
  reason, complete dead-letter timestamp fields, and positive generation.
- All stored offsets remain in `-840..840` minutes.
- Every delivery row must still reference one captured envelope row.

Create a partial dead-letter listing index matching the public order:

```text
state,
dead_lettered_at_utc_ticks DESC,
application_address,
message_id
```

Keep the existing eligibility index for pending/expired-lease selection.

### Transactional v1-to-v2 migration

The migration must:

- run under the existing exclusive write transaction;
- validate recognizable v1 metadata, columns, required eligibility index, and
  row invariants before conversion;
- reject unversioned, future, malformed, partially upgraded, or corrupt schema
  shapes deterministically;
- rebuild the delivery table where required to expand its state check and add
  the exact v2 constraints;
- copy every v1 pending, leased, and completed row without changing key, state,
  next attempt, lease token/owner/timestamps, attempt, delivered timestamp, or
  time offsets;
- initialize all existing rows with null dead-letter metadata and generation
  zero;
- recreate both required indexes;
- update the singleton version from 1 to 2 only after the new shape exists;
- commit only after cancellation and final validation checks;
- roll back completely on cancellation, SQL failure, invalid state, or invalid
  schema; never leave a half-migrated database.

Capture schema/tables and co-located durable-input tables must remain untouched.

### SQL transitions and operations

- Pending initialization for newly captured outputs writes null dead-letter
  metadata and generation zero.
- Eligibility selects only due pending rows or expired leased rows; completed
  and dead-lettered rows are never leased.
- Lease assignment, retry, and completion preserve generation and explicitly
  maintain valid null state fields.
- `DeadLetterAsync` is one atomic update guarded by key, leased state, exact
  token, and `lease_until > DeadLetteredAt`. It changes state to dead-lettered,
  clears lease/completion fields, stores reason and exact timestamp, and
  increments generation by one.
- An exact transition at or after lease expiry reports lease loss and does not
  mutate the row.
- `ListAsync` uses bound parameters, exact filters, the specified keyset order,
  and `PageSize + 1`; it selects metadata columns only.
- `GetAsync` joins the delivery and capture rows and returns the complete
  immutable envelope only for current dead letters.
- `ReplayAsync` is one write transaction and exact state/generation CAS. Resolve
  non-applied outcomes deterministically as not found, not dead-lettered, or
  generation mismatch; a matching CAS that updates no row is corruption.
- Two concurrent settlements or replays cannot both succeed.
- Reopening the provider preserves dead letters, generations, and replayed
  schedules.
- Busy/locked failures use the existing bounded busy-timeout behavior and name
  the operational action without exposing data.
- Disposal and concurrent lazy initialization retain current semantics.

## Tests And Test-Agent Evidence

Use the repository's existing xUnit, Shouldly, real temporary SQLite, and
deterministic `TimeProvider` conventions. Do not add sleeps, external services,
network access, random timing assumptions, or mocked SQLite behavior.

Before editing tests, the test workflow must run the Roslyn static pairing
analyzer once at the narrowest relevant root and record its heuristic output.
Maintain `.testagent/research.md`, `.testagent/plan.md`, and
`.testagent/status.md`. Treat pairing as a static source-to-test heuristic, not
line or branch coverage.

Required test groups include:

### Core contracts

- every constructor null/empty/whitespace/range/undefined-enum guard;
- immutable snapshot behavior for page items and complete envelopes;
- exact cursor/last-item validation;
- default and maximum page boundaries;
- inclusive lower and exclusive upper time filters at the contract boundary;
- result convenience properties and value equality;
- `MaxDeliveryAttempts` null/default, one, positive values, zero, and negative
  behavior.

### Registration

- builder callback exactly once and validation before mutation;
- default/null and explicit maximum frozen into immutable options;
- equivalent repeated delivery registration idempotency and different repeat
  rejection;
- SQL same-singleton aliases for capture, delivery, and dead-letter operations;
- duplicate/conflicting/tampered ownership rejection with no partial mutation;
- registration causes no file or schema I/O.

### Dispatcher

- unlimited mode retries and never calls `DeadLetterAsync`;
- maximum one dead-letters the first failure and does not retry;
- maximum N retries attempts below N and dead-letters attempt N;
- success at the limit completes and never dead-letters;
- cancellation leaves the lease unsettled;
- exact key/token/time/reason passed to dead-letter transition;
- wrong-key result becomes a store-contract failure;
- non-applied result is handled without false success;
- transition exception is wrapped as the named store operation and leaves the
  lease for expiry;
- logs remain metadata-only.

### SQL schema and migration

- fresh lazy schema is exactly v2 with columns, checks, foreign key, and both
  indexes;
- capture-only calls do not create delivery schema;
- lossless v1 migration for pending, leased, and completed rows, including
  attempts, tokens, timestamps, and non-zero offsets;
- migration rollback/cancellation and rejection of future, corrupt,
  unversioned, and partially upgraded shapes;
- shared database coexistence with capture and durable input.

### SQL state transitions and operations

- final failed lease atomically dead-letters with generation one;
- stale/wrong/expired token cannot dead-letter;
- completed, pending, missing, and already dead-lettered state outcomes;
- dead-letter rows are not eligible until explicit successful replay;
- metadata-only list filters, bounds, default/max page sizes, keyset pages, and
  deterministic equal-timestamp order;
- exact lookup missing/non-dead-letter behavior and full envelope fidelity;
- replay success, exact next schedule, attempt reset, envelope preservation,
  generation retention, and next lease attempt one;
- stale-generation rejection after a later failure cycle;
- not-found and not-dead-lettered replay statuses;
- concurrent transition/replay single-winner behavior;
- persistence across disposal/reopen;
- corruption and busy-timeout translation where the touched path introduces a
  distinct risk.

Every required behavior must map to at least one named test in
`.testagent/plan.md`. Tests must assert exact states, keys, timestamps, offsets,
generations, attempt counts, and absence of forbidden mutations—not merely that
an operation did not throw. Perform final pseudo-mutation gap analysis and
assertion-quality review on the touched tests. Report exact generated test names
as completion evidence.

## Documentation, Memory, And Governance

Update all affected public and internal records in the same round:

- root `README.md` package/capability summary;
- `src/FluxFlow.Engine.DurableOutput/README.md`;
- `src/FluxFlow.Engine.DurableOutput.SqlFile/README.md`;
- existing durable-output docs where guarantees or limitations changed;
- a focused documentation-site page for bounded failure, inspection, and replay
  (use the next appropriate numbered file and add it to `docs/README.md`);
- `docs/14-public-api-overview.md` and relevant architecture/reliability docs;
- `CHANGELOG.md` with both `2.0.0` entries and explicit breaking-provider note;
- `eng/packages.json` versions/dependencies/descriptions;
- `eng/public-api/baseline.txt` reviewed surface;
- `memory/00-index.md`, `memory/01-current-state.md`, and
  `memory/07-progress-log.md`;
- a new detailed `memory/276-durable-output-dead-letter-operations.md` containing
  decisions, schema/state semantics, exact verification evidence, limitations,
  and the next recommended step;
- `goals/README.md` so future accepted executable goals are always stored in a
  dedicated goal directory as a proper `README.md`.

Documentation must state clearly:

- capture remains separate from delivery;
- delivery remains opt-in, serial, and at-least-once;
- unlimited retry remains the default;
- a configured attempt limit counts leases/handler attempts and dead-letters on
  the final failed attempt;
- dead-letter summaries are metadata-only while exact lookup returns the full
  envelope;
- replay is explicit, one-record, generation-protected, and does not deliver
  immediately;
- handler idempotency by `DurableOutputEnvelope.Key` is still required because
  lease expiry can repeat destination delivery;
- there is no exactly-once destination guarantee, distributed transaction,
  automatic replay, retention, transport, parallel dispatch, workflow
  checkpoint, or multi-database provider in this round.

## Explicit Exclusions

Do not add or change:

- Engine output routing, port behavior, observation, or live dispatch order;
- workflow/application JSON schema or serialization;
- C# Fluent DSL or component authoring APIs;
- `FluxFlowApplicationOptions`;
- durable-input contracts or schema;
- HTTP, MQTT, AMQP, Kafka, broker, webhook, or destination adapter code;
- SQL Server, PostgreSQL, MongoDB, RavenDB, LiteDB, or another provider;
- batching, prefetch, parallel workers, partitions, sharding, leader election,
  distributed leases, or exactly-once claims;
- automatic replay, scheduled redrive, retention, purge, archival, compaction,
  operator UI/API/CLI, or authorization;
- variable backoff, jitter, per-address policies, handler classification, or
  reflection-based handler selection;
- workflow execution checkpoints, durable workflow state, compensation, or
  orchestration history;
- generic persistence repositories, universal provider abstractions, or new
  dependency graph layers.

## Validation And Completion Gates

The goal is complete only when all applicable gates pass:

1. The full goal exists at
   `goals/2026-08-01-durable-output-dead-letter-operations/README.md` before
   production edits.
2. Core and SQL-file production projects build in Debug and Release with no
   warnings.
3. Focused core and real-SQLite provider tests pass deterministically.
4. New tests are discovered through the solution-level harness, not only by
   direct project invocation.
5. Final pseudo-mutation and assertion-quality reviews find no unresolved
   high-risk gap in the new state, boundary, concurrency, or migration logic.
6. `dotnet format` verification passes for touched projects/files.
7. Reviewed public API baseline and package manifest validation pass.
8. Release governance tests pass.
9. Serialized non-incremental Debug and Release full-solution builds pass with
   zero warnings.
10. The serialized full Release test suite passes.
11. Both `2.0.0` packages are produced and inspected for expected assemblies,
    XML documentation, README content, dependency versions, symbols, archives,
    and absence of test/internal artifacts.
12. Documentation, documentation-site navigation, changelog, memory index,
    current state, progress log, detailed memory record, and goal convention are
    updated consistently.
13. Diff/status review confirms only intended files were changed by this round
    and all unrelated dirty-worktree changes were preserved.

## Required Final Report

Report:

- the goal README path;
- the final core and SQL-file API/behavior summary;
- the chosen breaking version rationale;
- schema v2 and migration result;
- focused and full build/test counts;
- package/governance/documentation results;
- a compact `Requirement | Evidence` table naming exact generated tests;
- any remaining limitations;
- the next recommended independent step.

Do not claim completion from compilation alone. Mark the execution goal complete
only after every required implementation, test, documentation, memory,
governance, packaging, and final validation task is genuinely finished.
