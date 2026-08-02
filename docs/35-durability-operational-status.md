# Durability Operational Status

FluxFlow durable stores expose optional read-only status capabilities for
operators that need backlog and lease visibility without querying
provider-owned tables:

- `IDurableInputStatusStore` for durable inbox state;
- `IDurableOutputStatusStore` for durable capture and delivery state.

These capabilities are hosting and operations boundaries. They do not change
Engine, workflow definitions, JSON, the C# DSL, component options, delivery
semantics, or acknowledgement behavior.

## Explicit Observation Time

The caller supplies the time boundary. This keeps tests deterministic and
prevents a persistence provider from owning another clock:

```csharp
var observedAt = timeProvider.GetUtcNow();

var inputStatus = serviceProvider
    .GetRequiredService<IDurableInputStatusStore>();
var input = await inputStatus.GetStatusAsync(
    new DurableInputStatusQuery(observedAt),
    cancellationToken);

var outputStatus = serviceProvider
    .GetRequiredService<IDurableOutputStatusStore>();
var output = await outputStatus.GetStatusAsync(
    new DurableOutputStatusQuery(observedAt),
    cancellationToken);
```

The snapshot preserves the exact `ObservedAt` value and offset. Persisted due
and lease times are returned as UTC instants.

## Input Snapshot

`DurableInputStatusSnapshot` reports:

- all pending inputs and the subset ready at or before the observation time;
- all leased inputs and the subset whose lease expires at or before that time;
- delivered idempotency tombstones and current dead letters;
- the oldest effective ready time;
- the next strictly future active lease expiry; and
- the checked total number of persisted inputs.

Exact-expiry leases are expired, not active. A pending record whose due time is
exactly the observation time is ready.

## Output Snapshot

`DurableOutputStatusSnapshot` separately reports:

- every immutable capture;
- captures not yet materialized into delivery state and their ready subset;
- pending, leased, completed, and dead-lettered delivery states;
- ready pending and expired-lease subsets;
- the oldest effective ready time and next active lease expiry;
- checked tracked-delivery and ready totals.

An unmaterialized output is expected between capture and delivery backfill. It
is not data loss. The status boundary makes that state visible without
triggering backfill.

## Read-Only Provider Behavior

The SQL-file and T-SQL providers expose status as another alias of their
existing container-owned singleton. Registration and resolution perform no
I/O.

A status call:

- opens one operation-scoped connection for a read-only operation;
- runs an aggregate query over state and timing columns only;
- honors cancellation and provider timeouts;
- does not enqueue, lease, settle, replay, or cache a record;
- does not create, migrate, repair, or backfill schema; and
- never selects payloads, headers, keys, lease tokens/owners, or failure text.

SQL-file output performs a read-only catalog check because capture and delivery
schemas are intentionally independent. When the delivery table is absent, all
captures are reported as unmaterialized and the table remains absent.

Because inspection is non-mutating, a missing or incompatible database/schema
fails visibly. Initialize or deploy schema through the provider's normal
documented lifecycle before relying on status polling.

## Polling Guidance

Status is an exact aggregate query, not a continuously maintained metrics row.
Poll at an operational interval appropriate to database size and load; do not
call it on every message. The durable packages separately publish event-driven
BCL counters, duration histograms, and activities for live capture/dispatch
work. Those signals require no status query and do not replace an exact backlog
snapshot.

A host may translate snapshots into its own health or metrics system and may
attach its preferred listener/exporter to the package-local instruments, but
FluxFlow does not register a poller, ASP.NET health check, exporter, timer,
cache, or dashboard. Instrumentation never queries provider status or adds
provider I/O.

Operational snapshots are observations, not transaction barriers. State can
change immediately after a snapshot returns.

The runnable
[`FluxFlow.DurabilityOperationsSample`](../samples/FluxFlow.DurabilityOperationsSample/README.md)
uses this boundary at intentional points only: once after enqueue but before
host startup, then once after causal input/output completion. It installs no
status timer, gauge callback, health check, or background poller. That pattern
keeps query cadence and database cost under host policy while the separate BCL
metrics and activities remain event-driven.

## Deliberate Limits

Status does not itself purge, archive, automatically replay, transport, deliver
in parallel, checkpoint workflows, or create distributed transactions.
Terminal tombstones and dead letters remain until a host explicitly invokes
the separate bounded retention capability.

See also:

- [Optional Durable Inputs](25-durable-inputs.md)
- [SQL-File Durable Inputs](26-sql-file-durable-inputs.md)
- [Optional Durable Output Delivery](29-durable-output-delivery.md)
- [T-SQL Durable Outputs](32-tsql-durable-outputs.md)
- [T-SQL Durable Inputs](34-tsql-durable-inputs.md)
- [Durable Terminal Retention](36-durable-terminal-retention.md)
- [Durable Output Lease Renewal](37-durable-output-lease-renewal.md)
