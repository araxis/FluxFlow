# Durable Output Lease Renewal

`FluxFlow.Engine.DurableOutput` renews an active output lease only when one
host-owned delivery handler runs longer than the configured renewal interval.
This keeps the ordinary in-process path and short durable deliveries small while
preventing a healthy long-running handler from losing ownership merely because
its initial lease duration elapsed.

Renewal is part of the optional durable-output delivery boundary. It does not
change Engine, workflow definitions, JSON, the C# DSL, component settings, or
`FluxFlowApplicationOptions`, and it adds no transport, queue, worker, ORM,
reflection, or provider-neutral database abstraction.

## Flat Configuration

```csharp
services.AddFluxFlowDurableOutputDelivery(delivery =>
{
    delivery.LeaseDuration = TimeSpan.FromSeconds(30);
    delivery.LeaseRenewalInterval = TimeSpan.FromSeconds(10);
    delivery.RetryDelay = TimeSpan.FromSeconds(5);
    delivery.IdleDelay = TimeSpan.FromMilliseconds(250);
    delivery.MaxDeliveryAttempts = 5;
});
```

`LeaseDuration` and `LeaseRenewalInterval` are sibling settings on the existing
flat builder. Both must be positive, and the renewal interval must be shorter
than the lease duration. Their defaults are 30 seconds and 10 seconds. The
builder validates first and freezes one immutable
`DurableOutputDeliveryOptions` record before DI mutation.

The interval is deliberately required when constructing the immutable options
record directly. Version 3.0 does not retain a compatibility overload that could
silently choose timing for a caller.

## Exact Store Transition

`DurableOutputDeliveryLeaseRenewal` contains only:

- the exact `DurableOutputKey`;
- the current non-empty lease token;
- the caller-owned `RenewedAt` observation time; and
- the requested `LeaseUntil`, which must be later than `RenewedAt`.

`IDurableOutputDeliveryStore.RenewLeaseAsync(...)` is a compare-and-set
transition. It returns `Applied` only when the key exists, is still leased by the
same token, and its current lease is unexpired at `RenewedAt`. An applied renewal
updates only the expiry value and offset. It does not change the owner, token,
attempt, state, envelope, schedule, failure information, or generation.

The remaining statuses keep normal races explicit:

| Status | Meaning |
|--------|---------|
| `LeaseLost` | The record is leased, but the token is stale or the lease expired. |
| `NotFound` | No delivery record exists for the exact key. |
| `InvalidState` | The record exists but is no longer leased. |

## Dispatcher Behavior

The existing serial dispatcher invokes the handler once with a linked
cancellation token. It then waits for either handler completion or the next
renewal interval using the same injected `TimeProvider` used for leasing and
settlement.

- Handler completion before the first interval performs no renewal call.
- Each tick requests expiry at `now + LeaseDuration` for the original exact
  key/token.
- `Applied` continues the same handler and schedules the next serial tick.
- Any non-applied result cancels and observes the handler and prevents stale
  completion, retry, or dead-letter settlement.
- A renewal store exception follows the same safe handler shutdown, sanitized
  store error reporting, and idle delay as another delivery-store failure.
- Host cancellation cancels and observes the handler and leaves persisted lease
  state untouched for normal expiry recovery.

There is at most one handler and one renewal call active for a dispatcher. The
implementation creates no parallel heartbeat task, internal queue, `Task.Run`,
timer service, or policy graph. If handler completion and a renewal tick race,
an already completed handler is preferred before issuing the next renewal. The
store remains the final authority when completion races an in-flight renewal.

Cancellation does not turn an ownership loss into a handler failure. Exceptions
observed while shutting down a stale handler are logged only by exception type;
payload, headers, exception message, and stack trace are not added to routine
logs.

## Provider Implementations

The SQL-file provider performs one direct update inside its existing immediate
write transaction. The T-SQL provider performs one direct parameterized update
through its existing transition path. Both use the same exact key/token/state/
expiry predicate and distinguish a zero-row update through their existing
status resolution. Neither changes its schema version, tables, indexes,
registration ownership, connection model, or dependencies.

A custom 2.x delivery provider upgrading to the 3.0 contract must implement the
same atomic transition and update direct construction of
`DurableOutputDeliveryOptions` with the required interval. It should verify:

- applied renewal extends only expiry;
- wrong token, exact expiry, and expired ownership return `LeaseLost`;
- missing keys and terminal/pending states return their exact statuses;
- cancellation and commit ownership follow the provider's existing boundary;
- concurrent renewal versus completion/retry/dead-letter has one authoritative
  result; and
- registration and resolution remain side-effect-free.

## Guarantee Boundary

Renewal reduces duplicate delivery caused by a healthy handler outliving its
initial lease. It does not make delivery exactly once. A destination side effect
can still succeed before a process or store failure prevents completion from
committing. Handlers should continue to use `DurableOutputEnvelope.Key` as an
idempotency identity when the destination supports it.

Handler cancellation is cooperative. FluxFlow neither forcibly terminates host
code nor revokes an external side effect that already occurred; observing the
handler only ensures that its task is not abandoned by the dispatcher.

See also:

- [Optional Durable Output Delivery](29-durable-output-delivery.md)
- [SQL-File Durable Outputs](28-sql-file-durable-outputs.md)
- [T-SQL Durable Outputs](32-tsql-durable-outputs.md)
- [Durability Operational Status](35-durability-operational-status.md)
- [Durable Terminal Retention](36-durable-terminal-retention.md)
