# Production T-SQL Durable Input Provider

Date: 2026-08-01

## Decision

Add `FluxFlow.Engine.DurableInput.TSql` 1.0.0 as a separate opt-in provider for
applications that need several processes or machines to share one durable
inbox. Preserve the provider-neutral core and local SQL-file provider without
changes.

## Boundary

- The package implements `IDurableInputStore`,
  `IDurableInputDeadLetterStore`, and
  `IDurableInputLeaseRenewalStore` through one DI-owned singleton.
- Registration is one flat `AddFluxFlowTSqlDurableInput(...)` callback. A
  short-lived mutable builder produces immutable redacted options.
- Registration and resolution are atomic, normalized-equivalent idempotent,
  tamper-aware, and perform no network or schema operation.
- Provider settings remain outside `FluxFlowApplicationOptions`.
- Engine, the durable dispatcher, JSON/C# definitions, components, and the
  existing SQL-file provider do not depend on this package.

## Persistence Design

- Direct parameterized `Microsoft.Data.SqlClient` commands; no ORM, micro-ORM,
  migrations framework, reflection, generic repository, or shared relational
  framework.
- Operation-scoped pooled connections with bounded command and schema-lock
  timeouts. Only official-client connection-open retry settings are exposed;
  state-changing commands and ambiguous commits are not retried.
- Two explicit `dbo` tables: version metadata and complete envelope plus
  operational state. Version 1 includes exact ordinal identity, envelope,
  attempt/retry state, lease ownership/token/times, settlement/failure fields,
  and dead-letter generation.
- `CreateOrMigrate` creates a completely absent known schema under a
  transaction-owned application lock. `ValidateOnly` performs no DDL. Partial,
  malformed, unsupported, future, and RCSI-enabled schemas fail closed.

## Concurrency Semantics

- Serializable key-range locking protects idempotent enqueue and conflict
  detection.
- Locking read committed with `UPDLOCK`, `READPAST`, and `ROWLOCK` acquires a
  deterministic due batch, creates one fresh token per row, and increments
  attempt exactly once. Concurrent hosts cannot own the same active lease.
- Complete, release, dead-letter, and renewal atomically match key, leased
  state, exact token, and unexpired lease.
- Dead-letter queries remain bounded and payload-free; exact lookup restores
  the full envelope; replay has a one-winner generation compare-and-set.
- Delivery remains at-least-once. Exactly-once execution, distributed
  transactions, internal workflow checkpoints, and automatic retention remain
  explicit non-goals.

## Validation Evidence

Implementation, repository, package, and real-server validation are complete.

- Fast provider tests pass 63/63 on `net8.0` and 63/63 on `net10.0`, with zero
  warnings.
- The explicit `net10.0` integration project builds cleanly and runs 64
  executions: 27 inherited conformance cases and 37 focused provider or
  environment executions. The complete real-server suite passes 64/64 with
  zero failures and zero skips.
- The test-quality review covered 70 locally declared tests and 317
  assertion-pattern occurrences, found no effective assertion-free or
  trivial-only test, and strengthened schema, ordering, persistence,
  corruption, and multi-store race coverage through pseudo-mutation analysis.
- Debug and Release solution builds pass across 133 projects with zero errors
  or warnings. A serialized Release run passes 2,267/2,267 default tests across
  66 test projects. Release governance passes 111/111.
- The 59-entry manifest and accepted public API baseline include the provider's
  34 intentional declarations. Package-manifest tests pass 4/4.
- The 1.0.0 package and symbols archive were created and inspected. Both target
  frameworks declare exactly DurableInput 1.1.0, SqlClient 7.0.2, and DI
  abstractions 10.0.7. Release/archive preflight, isolated-cache consumer smoke
  for both frameworks, feed verification preparation, and initial-package
  binary-compatibility preparation pass.
- Formatting, whitespace, and forbidden-pattern/dependency scans pass.

Docker Desktop later became available through `desktop-linux` with Engine
29.6.2. The first full run passed 59/64 and exposed five incorrect
provider-specific test expectations. Shared conformance and the established
SQL providers confirmed the production contract: a partial schema throws
`InvalidDataException`, and a losing transition observes `InvalidState` after
the winning transition changes the row out of `Leased`. Only those test
expectations were corrected; production code was unchanged. The repeated full
run passed 64/64 with zero failures and zero skips. It used SQL Server image
`mcr.microsoft.com/mssql/server:2022-latest` at immutable digest
`mcr.microsoft.com/mssql/server@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`,
and the runner cleaned up its owned container. No validation gate remains.

## Authoritative Goal

See
`goals/2026-08-01-production-tsql-durable-input-provider/README.md`.
