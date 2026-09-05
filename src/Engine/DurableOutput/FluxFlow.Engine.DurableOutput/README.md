# FluxFlow.Engine.DurableOutput

Optional provider-neutral capture and serial renewable leased at-least-once delivery for
explicitly selected FluxFlow application outputs. Capture, delivery, and
dead-letter administration are separate capabilities: a host pays only for the
parts it registers and uses.

## Registration

```csharp
using FluxFlow.Composition.Addressing;
using FluxFlow.Engine.DurableOutput;

services.AddSingleton<HostDurableOutputStore>();
services.AddSingleton<IDurableOutputStore>(provider =>
    provider.GetRequiredService<HostDurableOutputStore>());
services.AddSingleton<IDurableOutputDeliveryStore>(provider =>
    provider.GetRequiredService<HostDurableOutputStore>());
services.AddSingleton<IDurableOutputDeadLetterStore>(provider =>
    provider.GetRequiredService<HostDurableOutputStore>());

services
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

Registration is flat, validates and freezes immutable settings, and performs no
provider I/O. `LeaseRenewalInterval` must be positive and shorter than
`LeaseDuration`; the defaults are 10 seconds and 30 seconds respectively.
`MaxDeliveryAttempts` is nullable and defaults to `null`, which preserves
unlimited retry. A configured value must be positive.

Delivery activation requires exactly one `IDurableOutputDeliveryStore` and one
`IDurableOutputDeliveryHandler`. The delivery registration supplies neither.
`IDurableOutputDeadLetterStore` is an independently resolvable optional
operator capability; the dispatcher does not depend on it.

## Guarantees

For a selected output, `Enqueued` or equivalent-content `AlreadyExists` is
required before ordinary Engine output dispatch begins. `Conflict`,
serialization failure, or a store exception faults the output instead of
dispatching uncaptured data. Unselected outputs retain the lightweight
in-process path.

The optional dispatcher leases one output at a time and calls the host-owned
handler serially. A short handler incurs no renewal call. While a handler keeps
running, the dispatcher extends the exact current unexpired token at the
configured interval. If renewal reports ownership loss, the handler is canceled
and observed and no stale settlement is attempted. On success it completes the
current token. On handler failure
it retries after the fixed delay while the attempt is below a configured
maximum. Failure on the final configured attempt atomically dead-letters the
lease with the stable reason `HandlerFailure`. Unlimited mode never
dead-letters automatically.

Delivery remains at-least-once. If a destination accepts a side effect and the
process fails before completion commits, the same envelope can be delivered
again. Use `DurableOutputEnvelope.Key` as the destination idempotency key when
possible. A stopped process leaves an active lease for expiry recovery.

## Instrumentation

The package publishes BCL signals through `ActivitySource` and `Meter`, both
named `FluxFlow.Engine.DurableOutput`. Producer
`fluxflow.durable_output.capture` activities cover selected-output capture;
consumer `fluxflow.durable_output.deliver` activities cover leased delivery.
Counters and millisecond histograms describe capture results, leases, handler
results, delivery settlements/ownership loss, renewal results, store failures,
and capture/delivery duration.

Metric dimensions are bounded semantic outcomes, results, and store operation
names. They exclude addresses, contracts, message/tracing identities, payloads,
headers, lease data, provider settings, exception text, connection details, and
secrets. The host owns listener/exporter configuration. The package registers
none, does not poll operational status, and isolates listener failure from
capture, handler, and settlement behavior.

## Dead-Letter Operations

Capable providers may expose `IDurableOutputDeadLetterStore`:

```csharp
var page = await deadLetters.ListAsync(
    new DurableOutputDeadLetterQuery(pageSize: 50),
    cancellationToken);

var details = await deadLetters.GetAsync(key, cancellationToken);

var now = clock.GetUtcNow();
var replay = await deadLetters.ReplayAsync(
    new DurableOutputReplay(
        key,
        expectedGeneration: details!.Generation,
        replayedAt: now,
        nextAttemptAt: now),
    cancellationToken);
```

Listing is bounded metadata-only keyset pagination. Exact lookup returns the
complete current dead-letter envelope. Replay is explicit, single-record, and
generation-protected; it returns the row to pending, resets the attempt count,
and does not invoke the handler immediately. There is no automatic replay.

## Provider Boundaries And Limits

`IDurableOutputStore` owns atomic idempotent capture.
`IDurableOutputDeliveryStore` separately owns lease, renew, complete, retry,
and dead-letter compare-and-set transitions. `IDurableOutputDeadLetterStore`
separately owns operator inspection and replay. A custom provider may support
capture without either later capability, or delivery without operator APIs.

The host owns `IDurableOutputDeliveryHandler`; this package has no transport or
destination adapter. It also has no exponential backoff, batching, parallel
dispatch, automatic replay or purge, administration endpoint/UI,
distributed coordination, workflow checkpoint, producer/business-state
transaction, or exactly-once guarantee. Provider settings and migrations stay
outside `FluxFlowApplicationOptions`.

The optional retention capability below is an explicit host-invoked operation,
not a dispatcher policy or automatic service.

Version 3.0 adds exact-token renewal to `IDurableOutputDeliveryStore` and makes
the flat renewal interval required by `DurableOutputDeliveryOptions`; custom
2.x delivery providers and direct options construction must adopt both changes.

## Verifying A Custom Provider

The repository's durable-output test project contains reusable provider
conformance suites for capture, delivery, and dead-letter operations. A custom
provider test project supplies a fresh explicit context for the capabilities it
implements and inherits the matching suites. The shared specification covers
idempotent capture, deterministic and exclusive leasing, exact lease renewal,
token/expiry compare-and-set transitions, retry and replay scheduling, terminal-state
eligibility, bounded metadata-only listing, exact envelope fidelity,
generation protection, keyset ordering, and one-winner concurrency.

These are behavioral tests, not a runtime provider framework. They use no
reflection or provider discovery and do not require capture, delivery, and
operator interfaces to be the same object. Each provider must separately test
its schema or document shape, migrations, registration ownership, locking,
corruption behavior, restart/persistence, deployment model, and resource
lifecycle against its real backend.

## Optional Operational Status

`IDurableOutputStatusStore` is a separate optional capability. It reports
immutable payload-free capture and delivery state at an explicit caller-owned
observation time:

```csharp
var statusStore = provider.GetRequiredService<IDurableOutputStatusStore>();
var status = await statusStore.GetStatusAsync(
    new DurableOutputStatusQuery(timeProvider.GetUtcNow()),
    cancellationToken);
```

The snapshot distinguishes captures not yet materialized for delivery from
pending, leased, completed, and dead-lettered delivery state. Inspection does
not backfill delivery state, lease work, replay a record, or expose envelope,
key, token, owner, or failure data.

## Optional Terminal Retention

`IDurableOutputRetentionStore` is a separate optional capability for explicit,
bounded deletion of completed or dead-lettered output captures:

```csharp
var retention = provider.GetRequiredService<IDurableOutputRetentionStore>();
var result = await retention.PurgeCompletedAsync(
    new DurableOutputRetentionRequest(
        terminalBefore: timeProvider.GetUtcNow().AddDays(-30),
        maxCount: 250),
    cancellationToken);
```

The provider selects only the requested terminal delivery state and deletes
the capture parent so its delivery row is removed atomically through the
existing cascade. Pending, leased, replayed, opposite-terminal, and
unmaterialized captures are preserved. The host owns scheduling and repetition.
Purging a completed capture ends its idempotency window; purging a dead letter
permanently removes its inspection and replay source.
