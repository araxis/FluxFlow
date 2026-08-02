# Goal: Add The Optional Durable-Output Delivery Foundation

Date: 2026-08-01
Status: accepted for execution

## Objective

Close the next core durability gap by adding a small, provider-neutral,
at-least-once delivery capability for outputs already captured by
`FluxFlow.Engine.DurableOutput`.

The implementation must preserve FluxFlow's lightweight in-process identity.
Normal Engine outputs, unconfigured output ports, capture-only hosts, workflow
definitions, application JSON, the C# DSL, components, durable input, and
`FluxFlowApplicationOptions` must retain their current behavior. Delivery is
strictly opt-in and must impose no worker, polling, delivery-table I/O, queue,
serialization, or transport cost when it is not registered.

This round must be useful end to end: when explicitly enabled, one hosted
dispatcher leases a captured envelope, invokes one host-owned delivery handler,
marks successful delivery durably, and schedules failed delivery for a later
retry. Process failure and lease expiry must recover unfinished work.

## Architectural Principles

- Apply KISS, SRP, OCP, ISP, IoC, and explicit ownership pragmatically.
- Prefer direct C#, immutable records, narrow interfaces, and explicit DI.
- Keep registration flat and familiar: one service registration plus one
  one-level options-builder callback.
- Avoid reflection, assembly scanning, dynamic activation, service locators in
  runtime logic, global registries, static mutable state, generic repositories,
  provider switches, strategy frameworks, and speculative abstractions.
- Use the standard .NET hosting, logging, dependency-injection, cancellation,
  and `TimeProvider` primitives already present in the repository.
- Add no third-party dependency and no new package/project unless a concrete
  unavoidable boundary requires one. The intended design extends the two
  existing durable-output packages.
- Preserve immutable runtime configuration. A temporary mutable builder may be
  used only during registration and must produce one validated immutable
  options snapshot.
- Preserve the current dirty worktree. Do not reset, revert, stage, commit,
  push, or rewrite unrelated user changes.

## Required Package Boundary

### `FluxFlow.Engine.DurableOutput`

Keep this package provider-neutral. Add:

- delivery lease/transition contracts;
- the separate optional `IDurableOutputDeliveryStore` capability;
- the transport-neutral `IDurableOutputDeliveryHandler` contract;
- immutable delivery options plus a temporary registration builder;
- `AddFluxFlowDurableOutputDelivery(...)`;
- one internal serial hosted dispatcher.

Do not change `IDurableOutputStore.EnqueueAsync(...)` or couple capture to
delivery state. Do not add provider, SQL, HTTP, MQTT, broker, or destination
knowledge.

Advance the package from `1.0.0` to the appropriate additive minor version and
update package metadata, release notes, manifest governance, and the reviewed
public API baseline.

### `FluxFlow.Engine.DurableOutput.SqlFile`

Keep one container-owned `SqlFileDurableOutputStore`. It must implement both:

- `IDurableOutputStore` for capture; and
- `IDurableOutputDeliveryStore` for the optional delivery capability.

Register both service interfaces as aliases of the same concrete singleton.
Equivalent repeated registration remains idempotent; conflicting or tampered
ownership fails before partial descriptors are appended.

Advance the package from `1.0.0` to the appropriate additive minor version and
update its package metadata, README, manifest/public-API governance, package
artifacts, and release evidence.

## Provider-Neutral Delivery Contracts

Use explicit immutable public contracts with constructor validation. Exact
names may be adjusted only when repository conventions require it, but the
semantic surface must remain this small:

### Lease request

`DurableOutputDeliveryLeaseRequest` contains:

- non-empty, trimmed `OwnerId`;
- `Now`;
- `LeaseUntil`, strictly later than `Now`.

There is no batch size. A store leases at most one record per call.

### Lease

`DurableOutputDeliveryLease` contains:

- the complete immutable `DurableOutputEnvelope`;
- a non-empty lease token;
- non-empty, trimmed owner identity;
- exact `LeasedAt` and `LeaseUntil` values;
- a strictly positive attempt number.

### Completion transition

`DurableOutputDeliveryTransition` contains:

- a valid `DurableOutputKey`;
- a non-empty lease token;
- `OccurredAt`.

### Retry transition

`DurableOutputDeliveryRetry` contains:

- a valid key;
- a non-empty lease token;
- `ReleasedAt`;
- `NextAttemptAt`, not earlier than `ReleasedAt`.

Do not persist exception text, stack traces, arbitrary handler data, or a
failure-policy object in this round.

### Transition result

`DurableOutputDeliveryTransitionStatus` contains only the states required for
compare-and-set settlement:

- `Applied`;
- `LeaseLost`;
- `NotFound`;
- `InvalidState`.

`DurableOutputDeliveryTransitionResult` contains the exact key, status, and an
`IsApplied` convenience property. Undefined enum values and invalid keys fail
deterministically.

### Store capability

Add a separate interface rather than enlarging `IDurableOutputStore`:

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
}
```

A custom provider may support capture without supporting delivery. Enabling the
delivery dispatcher with no delivery-capable store must fail clearly during
service activation.

### Handler

The host or a future transport adapter supplies exactly one handler:

```csharp
public interface IDurableOutputDeliveryHandler
{
    ValueTask DeliverAsync(
        DurableOutputEnvelope envelope,
        CancellationToken cancellationToken);
}
```

The foundation must not inspect handler types, create handlers dynamically, or
know destination-specific configuration.

## Flat Registration And Immutable Options

Provide:

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
    });
```

`DurableOutputDeliveryOptions` is an immutable record. Its builder exposes only:

- `LeaseDuration`;
- `RetryDelay`;
- `IdleDelay`.

All durations must be strictly positive. Conservative defaults should follow
existing durable-input conventions unless stronger repository evidence
requires otherwise.

`AddFluxFlowDurableOutputDelivery(...)` must:

- reject a null service collection or callback;
- invoke the callback exactly once;
- validate/freeze all values before changing the collection;
- return the original collection;
- be idempotent for equivalent configuration;
- reject different repeated configuration without partial mutations;
- add `TimeProvider.System` only when the host has not supplied a clock;
- register exactly one delivery hosted service through normal .NET hosting;
- register no handler and no provider implicitly.

At activation, require exactly one `IDurableOutputDeliveryStore` and exactly one
`IDurableOutputDeliveryHandler`. Missing or ambiguous dependencies must produce
clear deterministic exceptions. Composition-root DI factories are allowed;
runtime service location is not.

## Hosted Dispatcher Behavior

Implement one small internal hosted service. It is an orchestration shell, not
a framework:

```text
try lease one output
    no lease -> wait IdleDelay
    lease -> invoke handler
        handler succeeds -> complete current lease
        handler fails -> retry current lease at now + RetryDelay
```

Required behavior:

- Generate one unique owner identity per dispatcher instance.
- Use `TimeProvider.GetUtcNow()` for leases, retries, and deterministic tests.
- Lease exactly one output per store call.
- Process serially. Add no internal channel, queue, batch, Dataflow graph,
  parallel fan-out, `Task.Run`, timer, or concurrency setting.
- Immediately seek the next lease after successfully processing one.
- Wait `IdleDelay` when no work exists and after a handled store failure, so no
  busy loop is possible.
- Pass the host stopping token through lease and handler operations.
- On handler success, complete with the current key/token and current time.
- On a non-cancellation handler exception, log without exposing payload/header
  content and call `RetryAsync` with `NextAttemptAt = now + RetryDelay`.
- On host cancellation during handler execution, stop and leave the lease
  untouched; lease expiry provides recovery.
- If complete/retry returns the wrong key, treat it as a store-contract failure.
- A non-applied transition is an expected concurrency outcome: log it without
  falsely reporting success and continue.
- Store exceptions must be observable, delayed, and recoverable. Do not crash
  or spin the host for a transient provider failure.
- Do not swallow host cancellation.
- Log lifecycle, lease attempt, success, retry scheduling, lease loss, and store
  failure using metadata only. Never log payloads, error details, or headers.

The handler contract is at-least-once. If an external side effect succeeds and
the process fails before completion commits, the envelope may be delivered
again. Documentation must require handlers/transports to use
`DurableOutputKey` as an idempotency identity when their destination supports
it. Do not claim exactly-once delivery.

## SQL-File Delivery Persistence

### Independent lazy schema

Do not alter the existing output-envelope schema version or add delivery-state
columns to `fluxflow_durable_outputs`.

Create a separate delivery schema only on the first delivery-store operation:

- `fluxflow_durable_output_delivery_schema` version 1;
- `fluxflow_durable_output_deliveries`.

A host that registers or uses capture without enabling delivery must never
create, validate, read, or update these delivery tables.

Delivery initialization must first ensure the existing output schema exists,
then transactionally create or validate the delivery schema. Reject:

- an unversioned delivery table;
- missing required tables;
- missing, multiple, non-positive, older unsupported, or newer version rows;
- incompatible column names, affinities, nullability, or primary-key ordinals;
- structurally corrupt state rows encountered by an operation.

Do not silently repair or adopt incompatible objects.

### Delivery-state model

Use the same binary composite identity as the envelope:

- `application_address`;
- `message_id`.

Persist only the state needed for this round:

- pending, leased, or delivered state;
- next-attempt timestamp;
- current lease token and owner;
- leased-at and lease-until timestamps;
- positive attempt count after first lease;
- delivered-at timestamp for the completion tombstone.

Store exact timestamp offsets wherever a public delivery contract returns or
compares the timestamp. Enforce coherent nullable/state combinations with
database constraints where practical. Parameterize all values.

### First-use backfill

On every lease transaction, insert missing pending delivery rows from the
immutable output table before selecting work. This provides these semantics:

- outputs captured before delivery was enabled become eligible;
- outputs captured concurrently become eligible on a later poll;
- completed tombstones are never replaced;
- capture remains independent and performs no delivery-state write.

Deleting the complete delivery schema intentionally resets delivery history;
partial or unversioned schema deletion remains an error and is never silently
repaired.

### Atomic lease

`TryLeaseAsync(...)` uses one immediate write transaction:

1. Initialize missing pending rows from captured outputs.
2. Select at most one eligible row: pending and due, or leased and expired.
3. Order deterministically by next-attempt time, original capture time, binary
   address, then binary message id.
4. Assign a new non-empty token and owner.
5. Store exact lease times.
6. Increment the attempt exactly once.
7. Read the complete immutable envelope.
8. Perform a final cancellation check.
9. Commit with `CancellationToken.None` so committed ownership is not reported
   ambiguously as canceled.

Concurrent lease calls must never receive the same current lease. An expired
lease may be acquired again with a new token and incremented attempt.

### Atomic completion

`CompleteAsync(...)` performs a token-protected transition only when:

- the key exists;
- state is leased;
- the supplied token is current;
- the lease is still unexpired at `OccurredAt`.

On success, mark delivered, clear active lease fields, store delivered time,
and preserve the row as a tombstone so it cannot be leased again. Otherwise
return the exact non-throwing transition status. Commit uses the same final
cancellation/ownership rule.

### Atomic retry

`RetryAsync(...)` applies only to the same current unexpired lease. On success:

- return state to pending;
- store exact `NextAttemptAt`;
- clear active lease fields;
- preserve attempt count;
- do not persist exception text or payload content.

The row is not eligible before `NextAttemptAt`. A stale or expired token cannot
reschedule a newer lease.

### SQLite lifecycle

- Reuse the existing validated database path, creation flags, connection
  string, pooling, and busy timeout.
- Enable and use database constraints consistently.
- Translate busy/locked errors into the existing clear provider exception
  style.
- Keep operations safe across reopen and concurrent store instances.
- Store disposal remains idempotent, rejects later calls, and clears its pool.
- Preserve durable-input/output coexistence in one database file.

## Explicit Non-Goals

Do not add any of the following in this round:

- dead-letter state, listing, replay, or maximum-attempt policy;
- exponential backoff, jitter, resilience packages, or policy frameworks;
- batch leasing or batch settlement;
- parallel delivery or concurrency configuration;
- multiple destinations, subscriptions, routing, keyed handlers, or fan-out;
- HTTP, MQTT, broker, email, webhook, or other transport implementations;
- retention, purge, archival, or output replay;
- administration API, CLI, dashboard, or Designer UI;
- distributed leader election or coordination;
- producer/business-state transaction integration;
- workflow-completion acknowledgement or component checkpoints;
- exactly-once delivery claims;
- changes to Engine, workflow definitions, application JSON, C# DSL,
  components, durable input, or `FluxFlowApplicationOptions`.

## Testing Requirements

Use the mandatory testing-agent workflow with xUnit and Shouldly. Preserve the
existing test projects and conventions. Write `.testagent/research.md`,
`.testagent/plan.md`, and final `.testagent/status.md`. Run the mandatory static
source-to-test pairing analyzer once before generation, then perform final
pseudo-mutation and assertion-quality reviews.

### Provider-neutral contract tests

Cover every constructor and boundary:

- null/empty/whitespace/surrounding-whitespace owner;
- empty token;
- invalid key;
- lease-until equality and earlier boundary;
- zero/negative attempt;
- retry time equality and earlier boundary;
- undefined transition enums;
- exact properties and `IsApplied`.

### Registration tests

Cover:

- original collection return;
- callback invoked once;
- immutable snapshot of every setting;
- exact conservative defaults and positive validation;
- equivalent repeated registration idempotency;
- different repeated registration conflict without descriptor mutation;
- exactly one hosted service;
- host-supplied `TimeProvider` preservation;
- missing/ambiguous store and handler activation failures;
- no provider or handler implicit registration;
- no file/schema/task side effects during registration.

### Dispatcher tests

Use deterministic fake stores, handlers, clocks, logs, and causal gates. Cover:

- no lease waits and retries without spinning;
- one lease invokes the handler exactly once with the exact envelope/token;
- success completes after handler completion and then seeks more work;
- handler failure schedules the exact fixed retry time;
- host cancellation during the handler performs neither complete nor retry;
- lease/store failures are logged and delayed, then recover;
- wrong transition key becomes a store-contract failure;
- non-applied completion/retry does not log false success;
- strictly serial processing with no second handler call while the first is
  blocked;
- lifecycle start/stop is bounded and cancellation-aware;
- no payload/header/error details appear in logs.

Do not use wall-clock sleeps to prove behavior.

### SQL-file tests against real SQLite

Cover:

- capture-only registration and enqueue leave delivery tables absent;
- first delivery operation creates exact independent schema v1;
- existing output schema/version remains unchanged;
- pre-existing captured outputs are backfilled and leased;
- concurrently/newly captured output appears on later lease;
- exact envelope/lease fields, timestamps, offsets, owner, token, and attempt;
- deterministic ordering across due times/capture times/keys;
- not-due retry is skipped until the supplied time;
- completion tombstone prevents redelivery across reopen;
- retry reschedules and preserves attempt history;
- identical concurrent lease calls yield one winner;
- expired lease gets a new token and incremented attempt;
- stale/expired/current token completion and retry outcomes;
- missing and invalid-state outcomes;
- pre-cancellation causes no state mutation;
- commit ownership after successful return;
- external write lock busy timeout and recovery;
- disposal before/after use and file release;
- newer, older, unversioned, missing-row, missing-table, incompatible-column,
  and corrupt-state rejection without mutation;
- one database file safely contains durable input, immutable output capture,
  and output delivery state.

Use isolated temporary files, real transactions, deterministic time, and causal
coordination. Do not mock SQLite or add timing sleeps.

## Documentation And Repository Governance

Create a dedicated documentation page for durable-output delivery and update:

- root README package/capability guidance;
- `CHANGELOG.md`;
- `src/FluxFlow.Engine.DurableOutput/README.md`;
- `src/FluxFlow.Engine.DurableOutput.SqlFile/README.md`;
- Engine README where the optional durability map is described;
- docs index;
- public API overview;
- runtime architecture;
- durable-input/output roadmap pages and guarantee comparisons;
- package manifest and public API baseline;
- release/package convention documentation if required.

Documentation must clearly state:

- capture and delivery are independent registrations;
- delivery is one handler and serial by design in this version;
- delivery is at-least-once;
- handler idempotency responsibility;
- capture-only hosts create no delivery state;
- lease expiry and crash recovery behavior;
- fixed retry with no dead-letter/max-attempt policy yet;
- local SQL-file scope and how another provider implements the narrow store
  capability;
- every explicit non-goal above.

Create and index `memory/275-durable-output-delivery-foundation.md`. Update
`memory/00-index.md`, `memory/01-current-state.md`, and
`memory/07-progress-log.md` with the final design and exact verification
evidence. Keep this goal Markdown as the immutable original scope record.

## Validation And Acceptance Gates

Do not declare completion until all applicable gates pass:

1. Focused restore/build for DurableOutput and SQL-file provider projects.
2. Focused provider-neutral and real-SQLite tests.
3. Formatting verification for every touched C# project.
4. Mandatory test-gap and assertion-quality reviews with exact fixes/evidence.
5. Solution/harness discovery includes every generated test.
6. Public API baseline is reviewed, accepted intentionally, then reverified.
7. Package manifest, project conventions, documentation links/boundaries, and
   release governance tests pass.
8. Both changed packages pack for net8.0 and net10.0 with README, binaries,
   symbols, correct dependency metadata, and direct archive inspection.
9. Dependency review confirms no reflection/magic/provider/transport/resilience
   additions and no Engine dependency reversal.
10. Full serialized non-incremental Debug and Release solution builds pass with
    zero errors and zero warnings.
11. One final serialized Release solution test run passes with zero failures
    and zero warnings.
12. `git diff --check`, scoped trailing-whitespace inspection, and a final
    targeted status/diff review pass without changing unrelated work.

## Completion Definition

The goal is complete only when an explicitly configured host can restartably
deliver captured outputs through one host-owned handler with lease-based
at-least-once semantics; successful delivery is a durable tombstone; failure is
durably rescheduled; expired ownership is recovered; capture-only behavior is
unchanged; every behavior is documented and tested; and all repository gates
are green.
