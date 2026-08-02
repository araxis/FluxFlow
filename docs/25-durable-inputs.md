# Optional Durable Inputs

FluxFlow has two deliberately separate input paths:

| Path | Boundary | Process-crash recovery | Delivery model |
|------|----------|------------------------|----------------|
| `ApplicationPorts.SendAsync(...)` | Current in-process input buffer | No | Accepted once by the current runtime |
| `DurableApplicationInputs.EnqueueAsync(...)` | Host-provided `IDurableInputStore` | Yes, before dispatch settlement | Leased at-least-once |

Adding durability does not alter Engine, the canonical application JSON, the C#
DSL, or component options. The application still declares an ordinary typed
message input. The host chooses the durable ingress path at its external
boundary.

Durable acceptance occurs only after `IDurableInputStore.EnqueueAsync` commits.
Cancellation before that commit leaves ownership with the caller; cancellation
afterward does not retract the stored record. Durable acceptance is not Engine
input acceptance. Engine input acceptance is not workflow completion, terminal
output production, external side-effect completion, or broker acknowledgement.

## Setup

```csharp
services.AddFluxFlow(configuration);
services.AddSingleton<IDurableInputStore, YourDurableInputStore>();
services.AddFluxFlowDurableInput(options =>
{
    options.BatchSize = 64;
    options.LeaseDuration = TimeSpan.FromSeconds(30);
    options.PollInterval = TimeSpan.FromMilliseconds(250);
    options.RetryDelay = TimeSpan.FromSeconds(1);
    options.StoreFailureDelay = TimeSpan.FromSeconds(2);
    options.MaxDeliveryAttempts = 10;
});
services.AddFluxFlowDurableInputContract<Command>("example.command.v1");
```

For a production local provider without an external database server, add
`FluxFlow.Engine.DurableInput.SqlFile`:

```csharp
services.AddFluxFlowSqlFileDurableInput(store =>
{
    store.DatabasePath = "data/fluxflow-inputs.db";
    store.CreateDatabase = true;
    store.CreateDirectory = true;
    store.AllowAbsoluteDatabasePath = false;
    store.BusyTimeout = TimeSpan.FromSeconds(30);
});
```

See [SQL-File Durable Inputs](26-sql-file-durable-inputs.md) for schema,
concurrency, backup, and security guidance. For a shared networked store used by
multiple application processes, add `FluxFlow.Engine.DurableInput.TSql`:

```csharp
services.AddFluxFlowTSqlDurableInput(store =>
{
    store.ConnectionString =
        configuration.GetConnectionString("FluxFlowDurableInput");
    store.SchemaManagement =
        TSqlDurableInputSchemaManagement.ValidateOnly;
});
```

See [T-SQL Durable Inputs](34-tsql-durable-inputs.md) for schema deployment,
locking, permissions, and operational guidance. Custom stores continue to
register `IDurableInputStore` directly.

Contract names are explicit stable persistence identifiers. They are not CLR
assembly-qualified names, and dispatch does not use `Type.GetType`, reflection,
or assembly scanning. Register one name per payload type. Conflicting names or
payload registrations fail when the registry is resolved. The
`JsonTypeInfo<T>` overload supports source-generated serialization.

`AddFluxFlowDurableInput(...)` freezes the mutable builder into immutable
runtime options. A host may register its own `TimeProvider` before calling the
method for deterministic timing.

## Optional Workflow-Completion Acknowledgement

The default `EngineAccepted` mode preserves the original behavior and has no
completion or renewal dependency. When a host has an explicit terminal
business/workflow signal, it can select `WorkflowCompleted`:

```csharp
services.AddSingleton<IDurableInputCompletionSource, OrderCompletionSource>();

services.AddFluxFlowDurableInput(options =>
{
    options.AcknowledgementMode = DurableInputAcknowledgementMode.WorkflowCompleted;
    options.WorkflowCompletionTimeout = TimeSpan.FromMinutes(10);
    options.LeaseDuration = TimeSpan.FromSeconds(30);
    options.LeaseRenewalInterval = TimeSpan.FromSeconds(10);
});
```

This mode requires exactly one `IDurableInputCompletionSource` and one
`IDurableInputLeaseRenewalStore`. The dispatcher subscribes with the exact
lease before sending, processes one active input at a time, and renews that
token while it waits. Only `DurableInputCompletionResult.Completed` marks the
input delivered. Explicit failure, a faulted completion task, and timeout use
the existing retry/maximum-attempt policy; lease loss stops without a stale
settlement; shutdown leaves the lease to expire.

The completion source is host-owned and must correlate its terminal signal to
the exact lease token. FluxFlow does not infer completion from arbitrary
outputs, trace ids, queue emptiness, graph inspection, headers, or timing. See
[Durable-Input Workflow Completion](33-durable-input-workflow-completion.md) for
the complete contract and example.

## Persisted Envelope

Schema version 1 stores:

- target `ApplicationAddress` and stable contract name;
- `MessageId`, `TraceId`, optional `CorrelationId` and `CausationId`;
- original message timestamp and durable enqueue timestamp;
- immutable headers;
- exactly one value/error case: a JSON payload or structured `FlowError`.

`FlowMessage.Restore(...)` and `RestoreError(...)` rebuild the message without
generating replacement identity or time values. Header dictionaries are copied
on both create and restore.

## Lease And Transition Protocol

The store key is `(ApplicationAddress, MessageId)`. Enqueueing equivalent
content under that key returns `AlreadyExists`; different content returns
`Conflict`.

A lease request includes owner, current time, expiry, and maximum count. The
provider selects due pending records plus expired leases in deterministic
oldest-due order. Creating a lease atomically assigns a unique token and
increments `Attempt`.

`MarkDeliveredAsync`, `ReleaseAsync`, and `DeadLetterAsync` are compare-and-set
operations. Each must validate the current, unexpired lease token in the same
atomic operation that changes state. `LeaseLost`, `NotFound`, and `InvalidState`
are observable non-mutating results, never permission to overwrite a newer
owner.

Released records receive an explicit `NextAttemptAt`. Dead-letter records retain
a structured `DurableInputFailure` for provider-owned inspection or operations.

`IDurableInputLeaseRenewalStore` is a separate optional provider capability.
It atomically changes only the requested expiry for an exact current,
unexpired key/token. Adding it separately keeps existing `IDurableInputStore`
implementations valid for the default mode.

## Dispatcher Decisions

For every leased record the dispatcher re-reads current port metadata:

- message input with the exact registered payload type: restore and send;
- `Accepted`: mark delivered immediately in `EngineAccepted`; in
  `WorkflowCompleted`, wait for the explicit result while renewing the lease;
- `Full`, `Unavailable`, or `Completed`: release after `RetryDelay`;
- missing address: retry until `MaxDeliveryAttempts` because a later revision may add it;
- unknown contract, unsupported envelope schema, deserialization failure,
  output/signal address, or type mismatch: dead-letter immediately;
- any transient result at `MaxDeliveryAttempts`: dead-letter as
  `MaximumAttemptsExceeded`.

Processing is sequential and batch-bounded. Workflow-completion mode requests
one lease per cycle so later batch leases cannot expire behind a long-running
workflow. There is no unbounded channel,
per-message `Task.Run`, or parallel fan-out. Store-cycle failures are logged and
backed off by `StoreFailureDelay`; already leased records recover through expiry.
Cancellation stops new leasing. An in-flight lease may be left to expire when a
safe release cannot be completed during shutdown.

## Instrumentation

The package publishes provider-neutral signals through the BCL source and
meter named `FluxFlow.Engine.DurableInput`. No registration callback, exporter,
poller, or provider support is required.

| Instrument | Type and unit | Semantic tags |
|------------|---------------|---------------|
| `fluxflow.durable_input.leases.acquired` | counter, `{lease}` | none |
| `fluxflow.durable_input.messages` | counter, `{message}` | `outcome=delivered|retry|dead_letter`; retry/dead-letter also use `failure.kind` |
| `fluxflow.durable_input.lease.renewals` | counter, `{renewal}` | `result=applied|rejected` |
| `fluxflow.durable_input.store.failures` | counter, `{failure}` | `operation` |
| `fluxflow.durable_input.processing.duration` | histogram, `ms` | none |

The `fluxflow.durable_input.process` consumer activity spans processing of one
lease. It may contain `flow.trace_id`, `attempt`, and `acknowledgement.mode`;
an escaping cancellation or failure sets an error outcome. Settlement counters
are recorded only after the store accepts the corresponding compare-and-set
transition. Store failures count provider calls that throw before a valid
result is returned.

Metric tags never include addresses, contracts, message/trace/correlation/
causation ids, lease tokens or owners, payloads, headers, provider settings,
paths, connection details, or exception text. Activity and metric listener
failures cannot alter dispatch or settlement behavior.

## At-Least-Once Crash Window

There is no atomic transaction spanning a provider store and Engine's
in-process input buffer. If the process fails after `SendAsync` returns
`Accepted` but before `MarkDeliveredAsync` commits, the expired record is
eligible again. Redelivery keeps the original `MessageId`, enabling consumer
deduplication.

Workflow completion can move this crash window after the host's terminal
signal, but cannot remove it: a crash after terminal side effects and before
the delivered commit can still redeliver. The package therefore does not claim
exactly-once execution. It also does not
persist workflow state, outputs, revisions, broker acknowledgements, or
component resources. Those are separate concerns and remain outside this
lightweight optional foundation.

Concrete providers and any future durable-outbox design remain separate
packages with their own storage, migration, and operational contracts. The
SQL-file provider serves local single-machine hosts; the T-SQL provider serves
shared multi-process hosts.

## Optional Dead-Letter Operations

`IDurableInputDeadLetterStore` is an optional provider capability independent
from the delivery-facing `IDurableInputStore`. A custom store remains valid
without it. The SQL-file provider exposes both interfaces through the same
singleton and requires no additional builder or setting.

Listing is bounded, newest-first, and keyset-paginated by dead-letter time,
ordinal address, and ordinal message id. Queries may filter by exact address,
failure kind, inclusive lower time, and exclusive upper time. List summaries
contain operational metadata only. `GetAsync` is the deliberate exact-key path
that restores the complete persisted envelope.

Replay is explicit and single-record. The request carries the key, expected
positive dead-letter generation, operation time, and next-attempt time. The
store atomically verifies current DeadLettered state and generation, then
returns `Replayed`, `NotFound`, `NotDeadLettered`, or `GenerationMismatch`.
Success preserves the complete envelope and original enqueue time, resets the
attempt counter, and returns it to Pending at the requested time. A later
dead-letter increments the generation, preventing stale commands from opening
a newer occurrence.

Replay does not strengthen the at-least-once guarantee. Consumers still use
the preserved `MessageId` for deduplication. This capability adds no bulk or
automatic replay, delivered replay, audit history, endpoint, CLI, or UI.
Permanent terminal deletion is a separate explicit bounded operation; see
[Durable Terminal Retention](36-durable-terminal-retention.md).

## Related Output Guarantees

Read-only backlog and lease inspection is available through the separate
optional `IDurableInputStatusStore` capability. See
[Durability Operational Status](35-durability-operational-status.md). It does
not change input acknowledgement or delivery semantics.

Delivered tombstones and dead letters can be removed only by explicitly
resolving `IDurableInputRetentionStore`. Nothing schedules that operation, and
purging a delivered identity ends its deduplication window.


The separate output path now has two independent opt-in boundaries:

1. [Optional Durable Output Capture](27-durable-output-capture.md) persists
   selected outputs before normal live Engine dispatch.
2. [Optional Durable Output Delivery](29-durable-output-delivery.md) leases
   captured records serially to one host-owned handler with fixed retry and
   at-least-once crash recovery.

The local implementation is documented in
[SQL-File Durable Outputs](28-sql-file-durable-outputs.md). Output delivery does
not include dead-letter/max-attempt policy, transport adapters, workflow
completion acknowledgement, or component checkpoints. Each would require an
independent explicit contract. `ApplicationPorts.ReceiveAsync(...)` and
`ObserveAsync(...)` remain live host taps rather than persistence contracts;
the unconfigured output path remains lightweight.
