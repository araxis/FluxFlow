# Durable Input Dead-Letter Operations

Date: 2026-07-30

## Objective

Complete the next optional durability slice without making the normal Engine,
Nodes, JSON/C# DSL, or application configuration provider-aware. Preserve the
small `IDurableInputStore` delivery contract and add operational dead-letter
inspection and replay as a separate capability implemented by providers that
can support it.

The round must remain explicit and lightweight: immutable contracts, bounded
queries, short storage transactions, normal Microsoft dependency injection,
and no reflection discovery, generic repository, custom DI layer, nested
builder callbacks, or application-wide backend settings.

## Delivered Boundary

- `FluxFlow.Engine.DurableInput` now defines the optional
  `IDurableInputDeadLetterStore` capability beside the unchanged
  `IDurableInputStore` contract.
- Immutable constructor-validated records cover bounded queries, typed keyset
  cursors, metadata-only summaries, pages, complete details, replay requests,
  replay outcomes, and results.
- Listing defaults to 50 records and is capped at 200. It supports exact
  application-address and failure-kind filters, an inclusive lower time bound,
  an exclusive upper time bound, and keyset continuation.
- Ordering is dead-letter time descending, then ordinal application address and
  message identity. Providers fetch at most `PageSize + 1`; there is no offset,
  total count, or unbounded materialization contract.
- Summaries omit payloads, headers, failure descriptions, error details,
  tracing identities, and provider data. Exact-key lookup is the deliberate
  path that restores the complete envelope for a current dead letter.

## Replay Semantics

- Replay is a single-record atomic compare-and-set on key, current
  `DeadLettered` state, and positive expected dead-letter generation.
- Results are `Replayed`, `NotFound`, `NotDeadLettered`, or
  `GenerationMismatch`.
- Success returns the preserved envelope to `Pending`, schedules the explicit
  `NextAttemptAt`, resets the attempt budget, and clears lease, failure,
  delivered, and dead-letter state.
- Envelope content, key, enqueue time, and current generation remain unchanged.
  Every later successful dead-letter increments the generation, so a stale
  operator request cannot reopen a newer failure occurrence.
- Cancellation or storage failure before commit rolls back. Once committed,
  storage owns the replay even if caller interest disappears.
- Replay remains at-least-once. It is not workflow completion, exactly-once
  execution, automatic replay, bulk replay, delivered-record replay, retention,
  deletion, or audit history.

## SQL-File Provider

- `SqlFileDurableInputStore` implements both durable-input interfaces.
- Schema version 2 adds nonnegative `dead_letter_generation` with default zero
  and a partial listing index on state, dead-letter time descending, ordinal
  address, and message identity.
- New files create v2 directly. The transactional v1-to-v2 migration preserves
  every Pending, released Pending, Leased, Delivered, and DeadLettered row,
  maps existing dead letters to generation 1 and other rows to generation 0,
  creates the index, and updates the version last.
- Lazy concurrent initialization is serialized by SQLite. Cancelled and failed
  migrations roll back; unsupported newer, unversioned, missing, partial, and
  corrupt schemas are rejected rather than recreated, repaired, or downgraded.
- `AddFluxFlowSqlFileDurableInput(...)` remains the only flat registration
  method. Registration performs no filesystem or database I/O and aliases the
  same container-owned singleton through `SqlFileDurableInputStore`,
  `IDurableInputStore`, and `IDurableInputDeadLetterStore`.

## Documentation And Testing

- Public guidance is in `docs/24-reliable-in-process-delivery.md`,
  `docs/25-durable-inputs.md`, and `docs/26-sql-file-durable-inputs.md`, plus the
  Engine and both package READMEs.
- Independent testing added reusable provider-neutral operations conformance,
  real-file SQLite operations, v1 migration, concurrent replay, lock recovery,
  cancellation, corruption, privacy, registration, and dependency-boundary
  coverage.
- Thirty-seven new test methods execute as 51 new cases. The final focused
  suites passed 76 provider-neutral and 83 SQL-file tests.
- Serialized non-incremental Debug and Release builds each passed 125 projects
  with zero errors and warnings. The full Release suite passed 1,725 tests in
  60 projects with zero warnings.
- Both net8/net10 packages packed successfully. Format verification, public API
  acceptance and ordinary revalidation, package inspection, dependency
  direction, forbidden-pattern, logging/privacy, bounded-query, whitespace, and
  diff checks passed. Exact DL-01 through DL-17 evidence is recorded in
  `.testagent/status.md`.

## Next Recommended Durability Slice

The next priority is an optional durable outbox foundation for selected
application outputs, but it must start with the output-capture guarantee rather
than a transport dispatcher or SQLite schema.

The current `ApplicationPorts.ReceiveAsync(...)` and `ObserveAsync(...)` APIs
are live taps. Observation is deliberately bounded and faults on overflow; it
does not make output capture durable. The next design must therefore define an
explicit opt-in capture boundary that can await store acceptance before an
output is considered durably captured, while leaving every unconfigured output
on the current allocation-light path.

That round should first settle provider-neutral envelope identity, selected
output registration, atomic/idempotent enqueue semantics, capture failure and
backpressure behavior, revision/disposal ownership, and the exact guarantee
boundary. Transport delivery, retries, dead letters, SQLite persistence,
workflow-completion acknowledgement, checkpoints, and distributed providers
should remain separate later slices.
