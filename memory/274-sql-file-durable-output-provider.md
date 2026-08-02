# SQL-File Durable Output Provider

Date: 2026-07-30

## Objective

Add the first production durable-output store without changing Engine's normal
in-process path or enlarging the provider-neutral store contract. The provider
must be explicit, local, reflection-free, flat to configure, safe under
concurrency, and limited to atomic idempotent capture. Delivery, retry, leasing,
dead letters, replay, retention, and checkpoints remain separate work.

## Provider-Neutral Hardening

`DurableOutputEnvelope.HasSameContent(...)` is now the authoritative reusable
content comparison for provider conflict detection. It compares the canonical
address and message key, contract/schema identity, value or complete structured
error, structural JSON, trace/correlation/causation identity, the original
timestamp including its offset, and ordinal header names and values.
`CapturedAt` alone is excluded because separate capture attempts can persist the
same message content at different times.

The comparison is explicit and reflection-free. JSON object property order and
header enumeration order are irrelevant; JSON array order, numeric text,
ordinal names/values, timestamps, and every error field remain meaningful.

`FluxFlow.Engine.DurableOutput.Tests` now owns a reusable store-conformance
contract. Provider projects supply only a store context, while the shared suite
asserts key scoping, idempotency, conflict/no-overwrite behavior,
pre-cancellation, and null validation.

## SQL-File Provider

`FluxFlow.Engine.DurableOutput.SqlFile` 1.0.0 is the opt-in local single-file
provider. Its public authoring surface is intentionally small:

- immutable `SqlFileDurableOutputStoreOptions` record;
- temporary mutable `SqlFileDurableOutputStoreOptionsBuilder` used only by one
  flat registration callback;
- `AddFluxFlowSqlFileDurableOutput(Action<...>)`;
- concrete `SqlFileDurableOutputStore`, also registered as the one
  `IDurableOutputStore` singleton.

Settings remain provider-local. `DatabasePath` is required; relative paths are
the conservative default, absolute paths require explicit permission,
directory/database creation is explicit, and the busy timeout is positive and
bounded by the SQLite millisecond range. Registration validates and freezes
settings without touching the file system, opening a connection, creating a
schema, starting a task, or registering a hosted service. Equivalent repeated
registration is idempotent; conflicting settings, tampered ownership, or an
existing store fail before partial descriptors are appended.

## Persistence And Schema

The first enqueue lazily creates or validates transactional schema version 1:

- `fluxflow_durable_output_schema` owns the single positive version row;
- `fluxflow_durable_outputs` owns complete immutable envelopes under the binary
  composite key `(application_address, message_id)`.

The output table stores no delivery state. Its names are separate from durable
input, so both providers can safely share one database file. Initialization
rejects unversioned output tables, missing tables or version rows, multiple or
invalid metadata rows, incompatible columns, older unsupported versions, and
newer versions. Existing objects are never silently adopted or repaired.

Each enqueue uses one immediate write transaction. An absent key is inserted as
`Enqueued`; equivalent persisted content returns `AlreadyExists`; different
content returns `Conflict` without replacing the first committed row. Complete
value/error JSON, IDs, headers, and both timestamps with offsets survive reopen.
Corrupt rows fail deterministically during duplicate/conflict resolution.

Cancellation is honored through the final pre-commit check. The commit then
uses a non-cancelable token so a transaction cannot be durably accepted and
reported as canceled ambiguously. The configured busy timeout bounds lock
acquisition, and store disposal clears its connection pool.

## Extensibility Result

Shared databases and document stores remain straightforward independent
packages: implement the existing one-method `IDurableOutputStore`, preserve the
same three enqueue outcomes atomically, define provider-local immutable options
and a flat registration callback, and run the reusable conformance suite. No
Engine, workflow-definition, JSON, C# DSL, or output-capture declaration change
is required. A relational implementation can use a unique composite key and
transaction; a document implementation can use deterministic identity and
conditional creation or optimistic concurrency.

The host still selects exactly one output store. Keyed multi-provider routing
is deliberately absent until a concrete deployment requires its extra
ownership and configuration complexity.

## Test Evidence

The mandatory bounded testing workflow added 51 unique test methods producing
76 new cases with 256 Shouldly assertion sites. The pseudo-mutation and
assertion-quality audits found no active scoped survivor and no assertion-free,
trivial-only, timing-only, mocked-database, sleep-based, or success-only tests.

Focused results:

- `FluxFlow.Engine.DurableOutput.Tests`: 52 passed, zero warnings;
- `FluxFlow.Engine.DurableOutput.SqlFile.Tests`: 61 passed against real isolated
  database files, zero warnings;
- both focused projects passed formatting verification.

The provider evidence covers immutable configuration, atomic DI conflicts,
lazy I/O, exact value/error round trips, reopen, all meaningful conflict groups,
concurrent identical/conflicting/different-key writers, pre-cancellation,
post-success commit ownership, real external write locks and timeout recovery,
schema creation and corruption rejection, idempotent disposal, file release,
and durable-input/output coexistence in one file.

Repository results:

- 57-package manifest and 128-project solution inventory verified;
- public API baseline reviewed, accepted, and reverified;
- manifest, package-convention, and documentation gates: 20 passed;
- serialized non-incremental Debug and Release builds: zero errors/warnings;
- completed serialized Release suite: 1,846 tests in 62 test projects, zero
  warnings;
- `FluxFlow.Engine.DurableOutput.SqlFile.1.0.0` binary and symbol packages
  created; direct archive inspection found the README and net8.0/net10.0
  binaries.

One initial whole-suite process exceeded its ten-minute command window without
reporting a test failure. The bounded retry completed in 196.4 seconds with the
aggregate above.

## Documentation

Updated the repository README, changelog, package README, Engine README, public
API overview, runtime architecture, durability roadmap pages, docs index,
package manifest, public API baseline, current-state memory, progress log, and
this indexed record. `docs/28-sql-file-durable-outputs.md` is the complete user
and provider-author contract.

## Deliberately Deferred

- Delivery leasing and worker ownership.
- Retry scheduling and attempt metadata.
- Delivery dead-letter operations.
- Transport-specific MQTT, HTTP, or other adapters.
- Retention, replay, administration, CLI, and UI.
- Producer/business-state transaction integration.
- Workflow-completion acknowledgement and component checkpoints.

The next recommended round is a provider-neutral delivery contract and worker
boundary, designed as a separate opt-in capability so capture-only and ordinary
in-process hosts pay no delivery cost.
