# FluxFlow.Engine.DurableOutput.SqlFile

Production SQLite single-file implementation of `IDurableOutputStore`,
`IDurableOutputDeliveryStore`, `IDurableOutputDeadLetterStore`, and
`IDurableOutputStatusStore`, and `IDurableOutputRetentionStore` for local
FluxFlow hosts. One container-owned singleton provides all five aliases.

Use another provider for a shared network database, document store, or
distributed deployment. The provider-neutral capture declarations, dispatcher,
and operational contracts remain unchanged.

## Registration

```csharp
using FluxFlow.Composition.Addressing;
using FluxFlow.Engine.DurableOutput;
using FluxFlow.Engine.DurableOutput.SqlFile;

services
    .AddFluxFlowSqlFileDurableOutput(store =>
    {
        store.DatabasePath = "data/fluxflow-output.db";
        store.BusyTimeout = TimeSpan.FromSeconds(30);
    })
    .AddFluxFlowDurableOutput(outputs =>
    {
        outputs.Capture(
            ApplicationAddress.WorkflowPort("Orders", "Complete", "Output"),
            "orders.completed.v1",
            ApplicationJsonContext.Default.OrderCompleted);
    })
    .AddSingleton<IDurableOutputDeliveryHandler, OrderDeliveryHandler>()
    .AddFluxFlowDurableOutputDelivery(delivery =>
    {
        delivery.LeaseDuration = TimeSpan.FromMinutes(1);
        delivery.LeaseRenewalInterval = TimeSpan.FromSeconds(20);
        delivery.RetryDelay = TimeSpan.FromSeconds(10);
        delivery.IdleDelay = TimeSpan.FromMilliseconds(500);
        delivery.MaxDeliveryAttempts = 5;
    });
```

Capture and delivery remain independent. A capture-only host omits the handler
and delivery registration. Registration freezes settings but performs no file,
connection, schema, or background work. Merely resolving none of the delivery
or operational aliases causes no delivery-table I/O.

`DatabasePath` is required. Relative paths are allowed by default. Set
`AllowAbsoluteDatabasePath` when an absolute path is intentional.
`CreateDirectory` and `CreateDatabase` default to `true`.

## Persistence

The capture schema owns immutable envelopes and atomic `Enqueued`,
equivalent-content `AlreadyExists`, or no-overwrite `Conflict` results for the
binary `(application_address, message_id)` key.

The independent lazy delivery schema is version 2 and contains:

- `fluxflow_durable_output_delivery_schema`;
- `fluxflow_durable_output_deliveries`;
- `ix_fluxflow_durable_output_deliveries_eligibility`;
- `ix_fluxflow_durable_output_deliveries_dead_lettered`.

Version-1 delivery databases migrate transactionally. Existing pending, leased,
and completed rows retain keys, schedules, lease ownership/tokens, attempts,
timestamps, offsets, and completion tombstones. New dead-letter metadata starts
empty with generation zero. Capture and co-located durable-input tables are not
modified.

Every lease transaction backfills missing pending state from captured outputs,
selects at most one due or expired row deterministically, assigns a new token,
and increments the attempt. Renewal, completion, retry, and dead-letter
settlement require the exact current unexpired token. Renewal changes only the
lease-expiry columns and requires no schema migration. Dead-letter settlement stores only
`HandlerFailure`, an exact timestamp, and an incremented generation; it stores
no exception text.

## Inspection And Replay

Resolve `IDurableOutputDeadLetterStore` from DI to:

- list bounded payload-free summaries with exact filters and stable keyset
  pagination;
- retrieve one exact complete envelope by key; or
- replay one current dead letter with an expected generation and explicit next
  attempt time.

Replay returns the row to pending, resets its attempt to zero, clears failure
state, preserves its complete captured envelope and generation, and does not
deliver immediately. A stale operator view cannot replay a later dead-letter
cycle. Completed tombstones and already replayed rows are never replayed.

SQLite coordinates concurrent instances, enforces one-winner transitions, uses
the configured bounded busy timeout, and preserves cancellation before its
non-cancelable commit boundary. Store disposal is idempotent and clears its
connection pool.

## Provider Conformance

The SQL-file test project is the first concrete adapter for the reusable
durable-output capture, delivery, and dead-letter conformance suites. Thin
provider subclasses create a fresh temporary database and expose the applicable
narrow interfaces; the shared tests own backend-independent lease, renewal,
settlement, ordering, inspection, replay, and concurrency semantics.

SQLite-specific tests remain separate for the exact schema and indexes,
version-1-to-version-2 migration, registration aliases and conflicts, lazy
initialization, write-lock/busy-timeout behavior, corrupt rows, persistence and
reopen, connection-pool disposal, and durable input/output coexistence. A future
provider should follow the same split: pass the shared behavioral floor and add
focused tests for the risks of its own backend and deployment model.

## Scope

This provider is for local single-file deployment. Delivery is serial and
at-least-once; handlers remain responsible for destination idempotency. There is
no automatic replay or purge, transport adapter, variable backoff,
batching, parallel delivery, multi-destination routing, distributed coordinator,
business-state transaction, workflow checkpoint, or exactly-once guarantee.

Version 3.0 follows the breaking core renewal-interface/options change. It adds
no delivery schema version or index; existing version-2 databases remain valid.

## Read-Only Status

The same singleton implements `IDurableOutputStatusStore`. It reports captures,
unmaterialized records, delivery states, readiness, and lease expiry without
selecting envelope content. If the independent delivery table is absent,
captures are reported as unmaterialized and inspection leaves that table
absent; status never initializes or migrates either schema. Version 2.1 adds
this capability without another schema change.

## Explicit Terminal Retention

`IDurableOutputRetentionStore` resolves to the same singleton. A retention call
uses the existing lazy delivery-schema lifecycle, selects a deterministic
bounded batch of completed or dead-lettered delivery rows, and deletes their
capture parents in one immediate transaction. The existing foreign-key cascade
removes delivery rows atomically. Capture-only, pending, leased, replayed, and
opposite-terminal records are preserved.

Registration and resolution remain I/O-free, and no schema version or index is
added. Calling retention can initialize the existing delivery schema; a
capture-only host that never calls it remains capture-only. The host owns the
exclusive cutoff and schedule. Purging ends the identity's idempotency or
dead-letter replay window.
