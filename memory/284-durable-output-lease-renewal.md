# Durable Output Lease Renewal

Date: 2026-08-02

## Outcome

Durable-output delivery now keeps ownership alive while one long-running
host-owned handler remains active. The implementation is intentionally small:
one immutable request, one method on the existing cohesive delivery-store
contract, one required sibling timing setting, one serial heartbeat loop, and
one direct compare-and-set provider update.

Capture-only hosts and ordinary Engine outputs remain unchanged. The round adds
no reflection, ORM, generic repository, queue, second hosted service, parallel
worker, provider-neutral SQL layer, schema object, migration, application
option, workflow field, JSON field, C# DSL field, or transport dependency.

## Provider-Neutral Contract

`DurableOutputDeliveryLeaseRenewal` preserves the exact key, non-empty token,
caller-owned renewal observation time, and requested later expiry. It is a
sealed immutable record with constructor validation and get-only properties.

`IDurableOutputDeliveryStore.RenewLeaseAsync(...)` owns renewal beside lease,
complete, retry, and dead-letter because these transitions form one output
delivery lifecycle. A separate capability alias would add registration and
ownership complexity without a supported independent combination.

An applied renewal changes only expiry. `LeaseLost`, `NotFound`, and
`InvalidState` expose expected races without exceptions. Provider failures keep
the existing sanitized `DurableOutputDeliveryStoreException` boundary.

## Flat Configuration

`DurableOutputDeliveryOptions` and its temporary builder add
`LeaseRenewalInterval` beside `LeaseDuration`. Defaults are 10 seconds and 30
seconds respectively. Every duration must be positive, and the interval must be
shorter than the duration. Direct options construction requires the value; no
compatibility overload or hidden fallback was retained.

## Dispatcher Ownership

The existing serial dispatcher starts one handler and waits for either handler
completion or the next interval through `PeriodicTimer` and the injected
`TimeProvider`. A short handler makes no renewal call. Each tick renews the
original key/token to `now + LeaseDuration`.

`Applied` continues the same handler. Any non-applied status cancels and observes
the linked handler and suppresses stale completion, retry, or dead-letter
settlement. Renewal-store failure and host cancellation also cancel and observe
the handler; persisted lease state remains recoverable through expiry. Handler
shutdown exceptions are observed with privacy-safe exception-type logging.

There is no abandoned task and no additional worker or queue. An already
completed handler is preferred before issuing another renewal, while the store
remains authoritative for a race with an in-flight renewal or settlement.

## Providers

The SQL-file provider uses the existing lazy delivery-schema lifecycle and
immediate write transaction. The T-SQL provider uses its existing operation-
scoped connection and transition helper. Both issue one parameterized update
guarded by exact key, token, leased state, and `lease_until > renewedAt`, then
use existing status resolution when no row changes. Only expiry ticks and offset
are updated. Existing schema versions and deployment requirements remain valid.
The live lock test also required the shared T-SQL lease-transition path to
normalize a client `SqlException` to `OperationCanceledException` when the
supplied token is canceled; transaction disposal preserves rollback and the
same store recovers after the external lock is released.

## Versions And Compatibility

- `FluxFlow.Engine.DurableOutput` advances from 2.2.0 to 3.0.0.
- `FluxFlow.Engine.DurableOutput.SqlFile` advances from 2.2.0 to 3.0.0.
- `FluxFlow.Engine.DurableOutput.TSql` advances from 1.2.0 to 2.0.0.

The major versions honestly record the new delivery-store member and required
direct options-constructor argument. Custom providers implement the exact
atomic renewal transition; direct options callers supply the interval.

## Verification

Focused evidence passed: core durable output 162/162, SQL-file durable output
166/166, fast T-SQL 136/136 across `net8.0` and `net10.0`, and the exact version
guard 6/6. The full real SQL Server runner passed 117/117 with zero skips using
SQL Server 2022 digest
`sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`;
its owned container was removed. The serialized Release build covered 133
targets with zero errors and warnings.

Public API final check passed 2/2 after review and baseline acceptance. All
three release preflights, vulnerability scans, fresh package/symbol/archive
checks, and isolated-feed consumer dry-runs passed on both target frameworks.
Formatting passed for every touched production and test project, whitespace
and local documentation links were clean, and no dependency changed. The
available core 2.2.0 compatibility comparison reported only the intended major
interface-member addition and constructor removal; the predecessor SQL-file and
T-SQL packages were unavailable from configured feeds.

Repeated aggregate solution test attempts were affected by concurrent-machine
load: broad runs reached 2,452/2,453 with only the slow documentation sample
timing out, or 2,449/2,453 with four unrelated load-sensitive failures. Every
observed failure passed unchanged in isolation, including the three
documentation samples, MQTT/session cases, and `FluxFlow.Resilience.Tests`
(11/11). A final aggregate excluding documentation samples timed out while that
unchanged resilience project was active. This limitation is recorded rather
than represented as a one-command full-suite pass; all renewal, real-server,
release, build, package, and isolated regression evidence is green.

The assertion and pseudo-mutation audit found no requirement-level gap,
assertion-free renewal case, approximate timing assertion, sleep-based
synchronization, or abandoned dispatcher task.

## Guarantee Boundary

Renewal reduces lease-expiry redelivery while a healthy handler runs. It does
not create exactly-once delivery or atomicity with a destination side effect.
Handlers still own destination idempotency and should use the durable-output key
when the destination supports it. Cancellation is cooperative: FluxFlow does
not forcibly terminate handler code or revoke an external effect that already
occurred.
