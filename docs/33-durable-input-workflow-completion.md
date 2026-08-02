# Durable-Input Workflow Completion

Durable input 1.1 adds an explicit opt-in boundary for workflows whose inbox
record must remain leased after the Engine accepts the input.

## Choose The Acknowledgement Boundary

| Mode | Delivered tombstone is written when | Additional runtime requirements |
|------|--------------------------------------|---------------------------------|
| `EngineAccepted` | The current Engine input accepts the restored message | None; this remains the default |
| `WorkflowCompleted` | The registered completion source reports success for the exact leased attempt | One completion source and one lease-renewal store |

`EngineAccepted` is still the smallest and fastest durable path. It uses the
configured batch size and does not resolve, subscribe to, or call completion or
renewal services.

Use `WorkflowCompleted` only when the host has a real terminal boundary such as
an explicitly committed business operation. The mode is not enabled by a
workflow shape or output convention.

## Flat Registration

```csharp
services.AddFluxFlow(configuration);

services.AddSingleton<IDurableInputCompletionSource, OrderCompletionSource>();

services.AddFluxFlowDurableInput(options =>
{
    options.AcknowledgementMode = DurableInputAcknowledgementMode.WorkflowCompleted;
    options.WorkflowCompletionTimeout = TimeSpan.FromMinutes(10);
    options.LeaseDuration = TimeSpan.FromSeconds(30);
    options.LeaseRenewalInterval = TimeSpan.FromSeconds(10);
    options.RetryDelay = TimeSpan.FromSeconds(1);
    options.MaxDeliveryAttempts = 10;
});

services.AddFluxFlowSqlFileDurableInput(store =>
{
    store.DatabasePath = "data/fluxflow-inputs.db";
});

services.AddFluxFlowDurableInputContract<OrderSubmitted>("orders.submitted.v1");
```

The mutable builders exist only during registration. Resolved options remain
immutable. There is no nested callback and no setting moves into
`FluxFlowApplicationOptions`.

Both production providers register `IDurableInputLeaseRenewalStore`
automatically as an alias of their existing singleton. Use the SQL-file provider
for local single-machine persistence or the T-SQL provider for shared
multi-process persistence. A custom provider may keep implementing only
`IDurableInputStore` when it supports the default mode. To support workflow
completion it additionally registers exactly one renewal capability.

## Completion Source Contract

FluxFlow calls the source before sending the restored message:

```csharp
public sealed class OrderCompletionSource(IOrderCompletionHub hub)
    : IDurableInputCompletionSource
{
    public ValueTask<IDurableInputCompletionSubscription> SubscribeAsync(
        DurableInputLease lease,
        CancellationToken cancellationToken = default)
        => hub.SubscribeAsync(
            lease.Envelope.Key,
            lease.LeaseToken,
            cancellationToken);
}
```

`IOrderCompletionHub` in this example is host/domain infrastructure, not a
FluxFlow convention. Its terminal producer must publish the exact key and token
for the attempt it completed. A late signal carrying an older lease token must
not complete a newer attempt.

The returned subscription exposes
`Task<DurableInputCompletionResult> Completion` and is always disposed after
send, settlement, cancellation, lease loss, or failure. Return
`DurableInputCompletionResult.Completed` for success, or
`DurableInputCompletionResult.Failed("stable safe description")` for an
expected terminal failure. Failure descriptions are persisted; do not include
payloads, credentials, personal data, or raw exception text.

The completion source owns correlation to the application's terminal event.
FluxFlow deliberately does not infer completion from:

- an arbitrary workflow output;
- an empty input or node queue;
- graph topology or terminal-node discovery;
- `TraceId` alone;
- a timer or lack of activity;
- headers, type names, reflection, or assembly scanning.

Those signals can be ambiguous under branching, retries, revision changes, and
late results. The exact lease token makes attempt ownership explicit.

## Runtime Sequence

Workflow-completion mode processes one active durable input at a time:

1. Lease one due input.
2. Validate envelope schema, contract, current port, direction, and payload
   type.
3. Create the exact-attempt completion subscription.
4. Restore and send to the Engine input.
5. If the send is rejected, dispose the subscription and use the existing
   retry/dead-letter classification.
6. If accepted, wait for the explicit terminal result.
7. At each `LeaseRenewalInterval`, first prefer a completion that is already
   available; otherwise atomically renew to `clock now + LeaseDuration`.
8. Mark delivered only after explicit success.

`WorkflowCompletionTimeout` may be positive or
`Timeout.InfiniteTimeSpan`. The renewal interval must be positive and shorter
than the lease duration. Infinite timeout still renews until success, lease
loss, cancellation, or provider failure.

## Failure And Recovery

| Event | Result |
|-------|--------|
| Completion source cannot subscribe or returns an invalid subscription | Retry as `CompletionSourceUnavailable` |
| Completion reports failure or its task faults/cancels independently | Retry as `WorkflowCompletionFailed` |
| Completion timeout expires | Retry as `WorkflowCompletionTimedOut` |
| Configured maximum attempt is reached | Dead-letter as `MaximumAttemptsExceeded`, retaining the originating kind in the stable description |
| Renewal returns `LeaseLost`, `NotFound`, or `InvalidState` | Stop waiting; perform no stale settlement |
| Renewal store throws | Use the existing store-failure backoff; the lease recovers by expiry |
| Host cancellation/shutdown | Dispose the subscription and leave the lease to expire |

Subscription and completion exceptions are logged by type without persisting
their message. Subscription disposal failure is logged but cannot reverse an
already committed settlement or terminate later dispatch.

## Provider Renewal Contract

`IDurableInputLeaseRenewalStore.RenewLeaseAsync(...)` is an additive provider
capability. Its request contains the key, exact token, renewal time, and exact
requested expiry. A provider atomically applies it only when the record exists,
is currently leased with that token, and is strictly unexpired at the renewal
time. It changes only the expiry; attempts, owner, token, envelope, state,
failure, and terminal metadata remain unchanged.

The SQL-file implementation uses its existing token/expiry columns and current
schema version 2. No migration, new table, column, or index is required.

## Guarantee Boundary

Workflow completion changes when FluxFlow settles the inbox lease; it does not
make workflow state or side effects transactional with the inbox. A process can
fail after the terminal operation succeeds and before the delivered transition
commits. The lease then expires and the same message can run again.

Therefore:

- delivery remains at-least-once;
- handlers and external side effects remain idempotent;
- `MessageId` remains the stable deduplication identity;
- there is no exactly-once claim, durable checkpoint/resume, rollback, or
  distributed transaction.

For the default protocol and provider operations, continue with
[Optional Durable Inputs](25-durable-inputs.md) and
[SQL-File Durable Inputs](26-sql-file-durable-inputs.md).
