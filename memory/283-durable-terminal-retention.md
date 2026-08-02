# Durable Terminal Retention

Date: 2026-08-01

## Outcome

FluxFlow durable input and output providers expose separate optional retention
capabilities for explicit, permanent deletion of old terminal records in
bounded transactions. Hosts own every retention policy, cutoff, schedule,
batch loop, and monitoring decision; FluxFlow runs no cleanup automatically.

## Contracts

- `IDurableInputRetentionStore` deletes delivered tombstones or input dead
  letters through independent methods.
- `IDurableOutputRetentionStore` deletes completed or dead-lettered capture
  parents; the existing foreign key removes the associated delivery state.
- Immutable request records carry an exclusive caller-owned `TerminalBefore`,
  optional exact `ApplicationAddress`, and a 1-through-1,000 `MaxCount` with a
  default of 100.
- Immutable result records return only the non-negative `DeletedCount`. Hosts
  repeat while a full batch is returned, with no cross-call snapshot promise.
- Existing capture, delivery, dead-letter, lease-renewal, status, Engine, DSL,
  JSON, and application-option contracts remain unchanged.

## Provider Behavior

- All four SQL-file/T-SQL providers implement retention directly on their
  existing container-owned singleton and expose one exact DI alias.
- Candidate selection is deterministic by terminal UTC time, ordinal address,
  and ordinal message identifier. The cutoff is exclusive and address scope is
  exact.
- SQL-file uses one immediate write transaction and a parameterized set-based
  delete. T-SQL uses one locking-read-committed transaction with bounded
  `UPDLOCK`/`READPAST` selection and a set-based delete.
- Cancellation or failure before commit rolls back. After the final
  cancellation check, commit uses a non-cancelable token so the result is not
  ambiguous.
- Output retention deletes the capture parent rather than only delivery state,
  preventing delivery materialization from recreating terminal work.
- Pending, leased, retryable, replayed, opposite-terminal, missing-timestamp,
  and output capture-only/unmaterialized records are not eligible.

## Operational Consequences

- Purging a delivered input tombstone ends its deduplication window; the same
  durable identity can later be accepted as new.
- Purging a completed output capture ends its idempotency/history window; the
  same output identity can later be captured as new.
- Purging a dead letter is irreversible and removes its inspection/replay
  source. Replay first makes the row ineligible; purge first makes replay
  observe not found.
- Registration and service resolution remain I/O-free. Calling output
  retention may initialize the existing delivery schema, while hosts that
  never invoke it remain capture-only.

## Boundaries

This round adds no worker, timer, retention duration, automatic loop, archive,
soft delete, UI, endpoint, CLI, ORM, generic repository, reflection, provider
discovery, distributed lock, application option, schema version, migration,
table, column, or index. Direct provider SQL remains localized in focused
partial files.

The package lines advance additively: input core/SQL-file to 1.3.0, input T-SQL
to 1.2.0, output core/SQL-file to 2.2.0, and output T-SQL to 1.2.0. Obsolete
SQL-file vulnerability suppressions were removed after current resolved-graph
scans reported no vulnerable packages.

## Validation Evidence

- The focused six-project durability matrix passed 844/844 Release executions
  with zero failures, skips, or warnings across both supported target
  frameworks where applicable.
- The serialized full Release solution passed 2,424/2,424 tests across 66
  projects. The Release build traversed 133 projects with zero errors/warnings;
  solution formatting and whitespace checks were clean.
- The real T-SQL input and output runners passed 89/89 and 100/100 tests with
  zero skips against the recorded SQL Server 2022 digest. All owned containers
  and timeout diagnostics were removed.
- Public API check/accept/recheck passed. Release governance passed 117/117,
  the exact package-version guard passed 6/6, and package/documentation
  conventions passed 20/20.
- All six package lines passed preflight plus archive, symbol, isolated-feed,
  and fresh-cache consumer verification on `net8.0` and `net10.0`.
- Binary compatibility passed for input core 1.3.0 against 1.2.0. The other
  five predecessor packages were unavailable from configured feeds, so those
  comparisons remain explicitly unavailable rather than reported as passes.
- Both SQL-file provider vulnerability scans reported no vulnerable packages;
  no dependency was added and the obsolete suppressions were removed.
- The independent test inventory, gap analysis, pseudo-mutation review, and
  assertion-quality evidence are retained in `.testagent/status.md`.

The authoritative scope and final command evidence are in
`goals/2026-08-01-durable-terminal-retention/README.md`.
