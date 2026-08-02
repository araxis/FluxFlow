# Optional Durable Output Delivery

`FluxFlow.Engine.DurableOutput` optionally delivers already captured outputs
through one host-owned handler. It is a small serial leased at-least-once
dispatcher, not a transport framework. It does not change Engine, the canonical
application document, the C# DSL, component options, or
`FluxFlowApplicationOptions`.

## Choose The Boundary

| Requirement | Registration | Guarantee |
|-------------|--------------|-----------|
| Lowest-overhead output inside the current process | ordinary Engine output | bounded live delivery only |
| Persist selected outputs before live dispatch | `AddFluxFlowDurableOutput(...)` plus `IDurableOutputStore` | atomic idempotent capture |
| Restartably deliver captured outputs | handler, `IDurableOutputDeliveryStore`, and `AddFluxFlowDurableOutputDelivery(...)` | serial renewable leased at-least-once delivery |
| Stop unlimited poison-message retry | positive `MaxDeliveryAttempts` | final failed attempt becomes a durable dead letter |
| Inspect or replay dead letters | provider-supplied `IDurableOutputDeadLetterStore` | bounded inspection and generation-protected explicit replay |

Capture and delivery are independent. A capture-only host creates no delivery
state and owns no worker. Unlimited retry remains the default.

## Setup

```csharp
services
    .AddFluxFlowSqlFileDurableOutput(store =>
    {
        store.DatabasePath = "data/outputs.db";
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

The callback freezes one immutable options record before DI mutation. All
durations must be positive, and `LeaseRenewalInterval` must be shorter than
`LeaseDuration`. `LeaseDuration` defaults to 30 seconds and
`LeaseRenewalInterval` defaults to 10 seconds.
`MaxDeliveryAttempts = null` means unlimited retry. Equivalent registration is idempotent; conflicting repeats
fail without partial mutation. A host-provided `TimeProvider` is preserved.

Exactly one delivery store and handler are required when the hosted dispatcher
activates. Registration supplies neither and performs no provider I/O.

## Delivery Protocol

The dispatcher:

1. leases at most one pending-due or expired output;
2. waits `IdleDelay` when none exists;
3. passes the complete immutable envelope to the handler;
4. while that handler is still running, renews the exact current unexpired token
   every `LeaseRenewalInterval` to `now + LeaseDuration`;
5. completes the exact current token on success;
6. on non-cancellation handler failure, retries at `now + RetryDelay` while the
   one-based attempt is below the configured maximum;
7. dead-letters a failed final attempt with reason `HandlerFailure`; and
8. immediately seeks more work after settlement.

A handler that completes before the first interval causes no renewal call. The
dispatcher uses its existing `TimeProvider`, has only one active handler and one
renewal call at a time, and creates no additional worker or queue. A successful
renewal changes only the expiry; it does not change token, owner, attempt, state,
or captured content.

A maximum of one dead-letters the first failed attempt. A maximum of N retries
failed attempts 1 through N-1 and dead-letters failed attempt N. Success on N
still completes normally. Unlimited mode follows the prior fixed-retry path.

Completion, retry, and dead-lettering are mutually exclusive compare-and-set
settlements on the current unexpired key/token. Expected lease loss is not
reported as success. If a renewal returns `LeaseLost`, `NotFound`, or
`InvalidState`, the dispatcher cancels and observes the handler and performs no
completion, retry, or dead-letter transition for the stale token. A renewal
store failure follows the same safe handler shutdown and existing sanitized
store-failure/idle-delay path. Host cancellation leaves the lease untouched for expiry
recovery. Store failures are observable and followed by `IdleDelay`, avoiding
both a host crash and a busy loop.

The worker owns no internal queue, batch, parallel fan-out, `Task.Run`, timer,
reflection discovery, handler selection, or policy graph. Logs contain only
stable identity/lifecycle metadata and exception type, never envelope content or
handler exception text.

## Delivery Instrumentation

The dispatcher publishes provider-neutral BCL metrics through the meter named
`FluxFlow.Engine.DurableOutput`:

| Instrument | Type and unit | Semantic tags |
|------------|---------------|---------------|
| `fluxflow.durable_output.leases.acquired` | counter, `{lease}` | none |
| `fluxflow.durable_output.handler.calls` | counter, `{call}` | `result=succeeded|failed|canceled` |
| `fluxflow.durable_output.deliveries` | counter, `{message}` | `outcome=completed|retry|dead_letter|ownership_lost`; settlements also use `result=applied|rejected` |
| `fluxflow.durable_output.lease.renewals` | counter, `{renewal}` | `result=applied|rejected` |
| `fluxflow.durable_output.store.failures` | counter, `{failure}` | `operation` |
| `fluxflow.durable_output.delivery.duration` | histogram, `ms` | none |

The `fluxflow.durable_output.deliver` consumer activity spans one leased
delivery. It may carry `flow.trace_id` and `attempt`; its bounded `outcome`
describes completion, retry, dead-letter, ownership loss, cancellation, or an
escaping failure. A rejected settlement remains visible through the delivery
counter's `result` without being reported as an applied transition.

Metric tags exclude addresses, contracts, message/trace/correlation/causation
ids, lease tokens or owners, payloads, headers, exception text, provider
settings, paths, connection details, and credentials. Host listener failures
are isolated from handler invocation, lease ownership, and settlement.

## At-Least-Once Responsibility

If the destination accepts a side effect and the process fails before
completion commits, the envelope can be delivered again. Use
`DurableOutputEnvelope.Key` as the destination idempotency identity when
possible. Dead-lettering bounds handler attempts for one failure cycle; it does
not make destination delivery exactly once.

## Provider Contract

`IDurableOutputStore` owns capture. The separate
`IDurableOutputDeliveryStore` owns leasing plus renewal, completion, retry, and
dead-letter settlement. The separate `IDurableOutputDeadLetterStore` owns
operator inspection/replay and is not a dispatcher dependency. Providers may
offer these capabilities independently.

Adding `RenewLeaseAsync(...)` to `IDurableOutputDeliveryStore` and requiring the
flat renewal interval makes the 3.0 contract intentionally breaking for custom
2.x delivery providers and direct options construction. Providers must atomically
extend only an exact current unexpired token when upgrading.

The SQL-file provider uses independent lazy delivery schema version 2 and
transactionally migrates version 1. See
[SQL-File Durable Outputs](28-sql-file-durable-outputs.md) and
[Durable Output Dead-Letter Operations](30-durable-output-dead-letter-operations.md).
The T-SQL provider uses one shared version-1 schema and locking
read-committed leases for multiple host processes; see
[T-SQL Durable Outputs](32-tsql-durable-outputs.md).
The exact heartbeat and race contract is documented in
[Durable Output Lease Renewal](37-durable-output-lease-renewal.md).

## Deliberate Limits

There is no automatic replay or purge/archive, variable backoff, jitter,
batching, parallel dispatch, multi-destination routing, transport adapter,
operator endpoint/UI/CLI, distributed leader election, producer/business-state
transaction, workflow-completion acknowledgement, checkpoint, or exactly-once
claim.

Explicit bounded deletion of completed and dead-lettered captures is available
through the separate optional retention capability. It is not a dispatcher
behavior or delivery policy. See
[Durable Terminal Retention](36-durable-terminal-retention.md).
