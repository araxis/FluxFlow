# Durable Output Delivery Foundation

Date: 2026-08-01

## Objective

Add the smallest optional restartable delivery layer over immutable durable
output capture. Ordinary in-process output and capture-only hosts must remain
unchanged. The implementation must stay explicit, provider-neutral,
reflection-free, serial, and at-least-once, with no transport framework or
hidden policy graph.

The accepted scope was recorded before implementation in
`goals/2026-08-01-durable-output-delivery-foundation.md`. The new `goals/`
convention keeps each accepted executable goal as its own dated Markdown file.

## Provider-Neutral Boundary

`FluxFlow.Engine.DurableOutput` 1.1.0 now adds a separate delivery capability
without enlarging the capture-facing `IDurableOutputStore`:

- `DurableOutputDeliveryLeaseRequest` contains exact owner, now, and expiry.
- `DurableOutputDeliveryLease` contains the complete immutable envelope, one
  non-empty token, exact ownership times, and a positive attempt.
- completion and retry requests carry the exact key/token compare-and-set
  identity and operation times.
- transition results are only `Applied`, `LeaseLost`, `NotFound`, or
  `InvalidState`.
- `IDurableOutputDeliveryStore` leases at most one item and applies completion
  or retry transitions.
- `IDurableOutputDeliveryHandler` is the single host-owned destination seam.

All public contracts are immutable and constructor-validated. Delivery-store
support is optional: a provider can still implement capture alone.

## Registration And Ownership

`AddFluxFlowDurableOutputDelivery(...)` uses one flat builder action containing
only `LeaseDuration`, `RetryDelay`, and `IdleDelay`. The builder freezes into an
immutable record before service descriptors change. Durations are strictly
positive; equivalent repeated registration is idempotent; different repeated
registration fails atomically.

The registration preserves a host-supplied `TimeProvider`, owns exactly one
normal .NET hosted-service descriptor, and registers neither a store nor a
handler implicitly. Hosted-service activation receives enumerable dependencies
and deterministically requires exactly one delivery store and one handler. It
does not retain or use `IServiceProvider` at runtime.

## Serial Dispatcher

The internal dispatcher has one loop and no secondary queue:

1. Lease at most one due output.
2. Wait the exact idle delay when none exists or a store call fails.
3. Invoke the one handler serially.
4. Complete the current token on success.
5. Retry the current token at `now + RetryDelay` on non-cancellation failure.
6. Leave the lease untouched when host cancellation interrupts the handler.

It immediately requests more work after settlement. Wrong lease ownership or
transition keys become observable store-contract failures. Expected non-applied
transitions never log false success. Logs contain lifecycle, identity, attempt,
transition, and exception-type metadata only; they never include payloads,
headers, structured errors, or persisted handler exception text.

## SQL-File Delivery State

`FluxFlow.Engine.DurableOutput.SqlFile` 1.1.0 exposes its existing concrete
singleton through both store capabilities. The original capture schema remains
version 1 and contains no delivery columns. The first delivery operation lazily
creates or validates the independent delivery schema version 1:

- `fluxflow_durable_output_delivery_schema`;
- `fluxflow_durable_output_deliveries`;
- `ix_fluxflow_durable_output_deliveries_eligibility`.

Capture-only registration and enqueue never touch those objects. Every immediate
lease transaction backfills missing pending rows from immutable captures,
selects one due or expired row by next-attempt time, original capture time,
binary address, then binary message ID, assigns a new token and owner,
increments the attempt, and returns the complete envelope.

Completion and retry require the exact current unexpired token. Completion
clears ownership and retains a delivered tombstone. Retry clears ownership,
preserves attempt history, and stores the exact next-attempt time. Expired
leases receive a new token and incremented attempt. Final cancellation checks
precede non-cancelable commits so committed ownership is not reported
ambiguously as canceled.

The provider reuses existing path, creation, pooling, foreign-key, and busy
timeout behavior. Input, immutable output, and output-delivery tables coexist in
one file. Schema initialization rejects unversioned, older, newer, missing,
incompatible, and corrupt objects instead of silently adopting or repairing
them.

## Guarantee And Limits

Delivery is at-least-once. A destination side effect can succeed before the
completion tombstone commits, so handlers should use
`DurableOutputEnvelope.Key` as their destination idempotency identity when
possible.

This round adds no dead-letter/max-attempt policy, exponential backoff, jitter,
resilience framework, batching, parallelism, concurrency option, multiple
destinations, routing, keyed handlers, fan-out, HTTP/MQTT/broker/email/webhook
adapter, retention, purge, archive, replay, administration API, CLI, Designer
UI, distributed coordinator, producer/business-state transaction integration,
workflow-completion acknowledgement, component checkpoint, or exactly-once
claim. Engine, workflow JSON, the C# DSL, components, durable input, and
`FluxFlowApplicationOptions` remain unchanged.

## Test And Quality Evidence

The mandatory testing workflow added 43 methods producing 57 cases across five
new delivery test files and strengthened SQL registration/shared-file tests.
The one-time pre-edit Roslyn inventory covered 9 source files and 19 test files;
the final pseudo-mutation and assertion-quality audits found no active scoped
survivor or assertion-quality defect.

Focused results:

- `FluxFlow.Engine.DurableOutput.Tests`: 91 passed, zero warnings.
- `FluxFlow.Engine.DurableOutput.SqlFile.Tests`: 79 passed against real isolated
  SQLite files, zero warnings.
- both focused non-incremental builds and formatting checks passed.

Coverage includes contract bounds, immutable options, flat/idempotent/conflict
registration, exact-one DI resolution, strict dispatcher seriality, fake-clock
idle and retry boundaries, cancellation ownership, log privacy, lazy schema,
historic backfill, deterministic ordering, concurrent exclusivity, lease expiry,
token/attempt recovery, completion/retry compare-and-set outcomes, persistent
tombstones, retry due time, complete envelope round trips, busy-lock recovery,
disposal, schema corruption/version cases, and shared durable input/output files.

Repository and package results:

- public API baseline intentionally accepted and reverified: 2 passed;
- release governance, manifest, package, and documentation suite: 111 passed;
- both 1.1.0 binary and symbol packages created with README and net8.0/net10.0
  binaries; direct inspection and the repository archive checker passed;
- serialized non-incremental Debug build: 129 projects, zero errors/warnings;
- serialized non-incremental Release build: 129 projects, zero errors/warnings;
- serialized Release suite: 1,903 tests in 62 projects, zero warnings.

## Documentation

Updated the root/package/Engine READMEs, changelog, docs index, public API
overview, runtime architecture, reliability and durability comparison pages,
SQL-file provider guidance, public API baseline, current-state memory, progress
log, and this indexed record. `docs/29-durable-output-delivery.md` is the
complete user/provider delivery contract.
