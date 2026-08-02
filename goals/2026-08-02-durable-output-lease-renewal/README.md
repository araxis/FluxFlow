# GOAL: Renew durable-output delivery leases while a handler is running

## Status

- State: complete
- Date: 2026-08-02
- Repository: FluxFlow
- Scope: provider-neutral durable-output delivery plus the existing SQL-file
  and T-SQL durable-output providers
- Compatibility posture: an intentional, documented major-version change to
  the cohesive delivery-store contract and immutable delivery options

## Objective

Close the remaining long-running durable-output delivery reliability gap with
the smallest explicit design. Today the delivery dispatcher acquires one
time-bounded lease and awaits the host handler under that original expiry. If
the handler runs longer than `LeaseDuration`, another host may reclaim the
record while the first handler is still active. At-least-once delivery permits
duplicates, but avoidable overlapping attempts make handler idempotency harder
and reduce operational trust.

Add periodic compare-and-set renewal of the exact active delivery lease while
the handler is still running. Preserve FluxFlow's lightweight model:

- delivery remains serial and opt-in;
- short handlers complete without renewal-store I/O;
- providers update only the existing lease-expiry columns;
- no timer, worker, queue, or hosted service is added beyond the existing
  delivery dispatcher;
- no new interface, service alias, package dependency, schema object, or
  provider framework is introduced;
- destination handlers remain responsible for idempotency;
- no exactly-once, distributed-transaction, checkpoint, or side-effect
  cancellation guarantee is claimed.

The implementation must follow KISS, SRP, ISP, and dependency inversion. Use
direct, explicit C# and provider SQL. Avoid reflection, conventions, generic
repositories, inheritance frameworks, hidden background work, unowned tasks,
and nested configuration callbacks.

## Current-state evidence

`DurableOutputDeliveryDispatcher` currently:

1. creates a lease ending at `now + LeaseDuration`;
2. invokes `IDurableOutputDeliveryHandler.DeliverAsync(...)` once;
3. waits for that handler with no lease heartbeat;
4. completes, retries, or dead-letters through the original key/token.

`IDurableOutputDeliveryStore` already cohesively owns acquisition and all
lease-scoped state transitions: lease, complete, retry, and dead-letter.
Renewal belongs to that same lifecycle. Do not create
`IDurableOutputLeaseRenewalStore` or another DI alias merely to avoid a major
version. The user explicitly permits breaking changes when they produce a
simpler and more trustworthy API.

Both production providers already persist mutable lease-expiry ticks and
offsets and already use exact key/token/expiry compare-and-set settlement.
Renewal therefore requires no schema version, migration, table, column, index,
trigger, stored procedure, or provider option.

The mandatory pre-test-edit Roslyn pairing inventory ran once over the current
repository:

- 1,066 discovered C# files;
- 759 source files;
- 307 test files;
- 528 statically paired source files;
- 231 statically unpaired source files;
- 7,282 ms elapsed.

This is a syntax-based pairing heuristic, not line or branch coverage. Do not
rerun it after test edits to improve the number.

## Required public contract

### `DurableOutputDeliveryLeaseRenewal`

Add one public sealed immutable record to the provider-neutral durable-output
package:

```csharp
public sealed record DurableOutputDeliveryLeaseRenewal
{
    public DurableOutputDeliveryLeaseRenewal(
        DurableOutputKey key,
        Guid leaseToken,
        DateTimeOffset renewedAt,
        DateTimeOffset leaseUntil);

    public DurableOutputKey Key { get; }

    public Guid LeaseToken { get; }

    public DateTimeOffset RenewedAt { get; }

    public DateTimeOffset LeaseUntil { get; }
}
```

Validation and value semantics are exact:

- `Key` must contain a non-null application address and non-empty message id,
  using the existing delivery validation;
- `LeaseToken` must not be `Guid.Empty`;
- `LeaseUntil` must be strictly later than `RenewedAt` by instant;
- exact `DateTimeOffset` values, including offsets, are retained;
- the record is sealed and exposes get-only properties;
- ordinary record value equality is preserved;
- no owner id, attempt, payload, headers, failure text, or provider details are
  added to the request.

The contract requests the exact new expiry. A provider must not silently round,
clamp, add a duration, or require the requested expiry to be later than the old
expiry. A caller may deliberately shorten a still-current lease as long as the
new expiry is later than `RenewedAt`. The production dispatcher always requests
`clock now + LeaseDuration`.

### Cohesive delivery-store change

Add this member directly to `IDurableOutputDeliveryStore`:

```csharp
ValueTask<DurableOutputDeliveryTransitionResult> RenewLeaseAsync(
    DurableOutputDeliveryLeaseRenewal renewal,
    CancellationToken cancellationToken = default);
```

Return the existing `DurableOutputDeliveryTransitionResult` and existing
statuses:

- `Applied`: the key exists, is leased, has the exact token, is unexpired at
  `RenewedAt`, and its expiry was changed atomically to the exact requested
  value;
- `LeaseLost`: the record is leased but the token is not current, or the exact
  lease is expired at `RenewedAt`;
- `NotFound`: no delivery row exists for the key;
- `InvalidState`: the row exists but is not leased.

The method must never recreate a missing delivery, revive an expired lease,
change a token or owner, increment an attempt, change retry timing, change
terminal state, materialize a capture, deserialize a payload, or settle work.

Do not add a separate renewal capability interface. Renewal is inseparable
from the delivery store's existing lease lifecycle and every supported
delivery provider must implement it. This deliberately avoids a sixth provider
alias and another dispatcher dependency.

## Required flat configuration

Change the canonical immutable options constructor to:

```csharp
public DurableOutputDeliveryOptions(
    TimeSpan leaseDuration,
    TimeSpan leaseRenewalInterval,
    TimeSpan retryDelay,
    TimeSpan idleDelay,
    int? maxDeliveryAttempts = null);
```

Add:

```csharp
public TimeSpan LeaseRenewalInterval { get; }
```

Update `DurableOutputDeliveryOptionsBuilder` with one flat property:

```csharp
public TimeSpan LeaseRenewalInterval { get; set; }
```

Defaults are explicit:

- `LeaseDuration`: 30 seconds;
- `LeaseRenewalInterval`: 10 seconds;
- `RetryDelay`: 1 second;
- `IdleDelay`: 250 milliseconds;
- `MaxDeliveryAttempts`: null, retaining unlimited attempts.

Validation must require:

- every duration is greater than zero;
- `LeaseRenewalInterval` is strictly less than `LeaseDuration`;
- `MaxDeliveryAttempts`, when set, is positive.

Use the exact offending parameter/property name in validation exceptions. Do
not derive a renewal interval from the lease duration, hide a safety factor, or
silently correct invalid settings. Remove the old four-parameter constructor;
do not add a compatibility overload or obsolete forwarding API. The major
version communicates the intentional source/binary break.

The public registration remains one flat callback:

```csharp
services.AddFluxFlowDurableOutputDelivery(options =>
{
    options.LeaseDuration = TimeSpan.FromSeconds(30);
    options.LeaseRenewalInterval = TimeSpan.FromSeconds(10);
    options.RetryDelay = TimeSpan.FromSeconds(5);
    options.IdleDelay = TimeSpan.FromMilliseconds(250);
    options.MaxDeliveryAttempts = 5;
});
```

Do not add nested builders, named options, `IOptions<T>`, reflection, or
application/workflow/JSON/DSL configuration for this setting.

## Dispatcher behavior

Keep `DurableOutputDeliveryDispatcher` as the one serial orchestration shell.
Do not add another hosted service or background task owner.

For each lease:

1. Create one linked per-attempt cancellation source from the host stopping
   token.
2. Invoke the handler exactly once with the linked attempt token.
3. Wait for either handler completion or one `LeaseRenewalInterval` delay using
   the injected `TimeProvider`; do not use wall-clock sleeps in tests.
4. Always check handler completion before issuing a renewal. If handler and
   renewal delay become ready together, prefer the completed handler and avoid
   the unnecessary store call.
5. If the handler remains incomplete, construct a renewal using the original
   key/token, current `TimeProvider` time, and
   `now + LeaseDuration`.
6. Invoke `RenewLeaseAsync(...)` through the existing store exception wrapper
   with the operation name `renew-lease`.
7. Validate that every returned transition key exactly matches the leased key.
8. On `Applied`, continue waiting under the same handler, key, token, owner,
   and attempt.
9. On `LeaseLost`, `NotFound`, or `InvalidState`, cancel the handler's linked
   token, observe the handler task to completion, log the loss without payload
   or secrets, and perform no completion/retry/dead-letter transition.
10. On renewal-store failure, cancel and observe the handler, then surface the
    existing sanitized store exception so the dispatcher applies its existing
    idle delay and the record recovers only through lease expiry.
11. On host cancellation, cancel and observe the handler and leave the lease
    untouched for expiry recovery.
12. When the handler completes successfully while ownership is still believed
    current, use the existing completion transition. The store remains the
    final authority and may return lease loss if expiry/reassignment won a
    race.
13. When the handler fails while ownership is still believed current, preserve
    the existing retry or maximum-attempt dead-letter decision. Those store
    transitions remain the final authority.

No handler task may be abandoned, fire-and-forgotten, or left unobserved. A
handler that ignores cancellation may still delay dispatcher shutdown or
ownership-loss recovery; document that handlers must cooperate with their
cancellation token. Do not use `Task.Run`, polling spins, a second queue,
parallel handler execution, or detached continuations.

Renewal reduces avoidable overlapping attempts but cannot revoke an external
side effect that has already occurred. A lease can still be lost because of
clock/configuration/database stalls or races. The guarantee remains
at-least-once, and the destination must use `DurableOutputEnvelope.Key` as an
idempotency key whenever possible.

## SQL-file provider behavior

Implement `RenewLeaseAsync(...)` in the existing focused delivery partial.

Requirements:

- validate null and pre-cancellation before any initialization or I/O;
- use the existing lazy delivery-schema initialization path;
- open an operation-scoped connection;
- use one immediate write transaction;
- issue one parameterized `UPDATE` scoped by exact ordinal address/message id,
  leased state, exact token, and `lease_until_utc_ticks > renewedAt.UtcTicks`;
- update only `lease_until_utc_ticks` and
  `lease_until_offset_minutes` to the exact requested values;
- if no row is updated, use the existing transition-status resolution logic in
  the same transaction;
- perform a final cancellation check before commit and commit with
  `CancellationToken.None` to avoid ambiguous cancellation after ownership
  passes to the provider;
- translate SQLite busy/locked failures through the existing provider
  exception path with an operation-specific renewal description;
- preserve schema versions 1 (capture) and 2 (delivery), table/index/check
  shapes, all non-expiry columns, and capture-only behavior.

Renewal must not read or deserialize the envelope payload or headers.

## T-SQL provider behavior

Implement the same cohesive member in the existing delivery partial.

Requirements:

- validate null/pre-cancellation before schema/network I/O;
- use the existing explicit initialization mode and operation-scoped pooled
  connection;
- use one read-committed transaction and one parameterized exact-key/token/
  unexpired-state `UPDATE`;
- update only expiry ticks and offset;
- resolve zero-row results through the existing exact transition-status query
  within the same transaction;
- retain current command timeout, schema governance, redaction, and
  cancellation-before-noncancelable-commit semantics;
- add no automatic command retry because an interrupted state-changing commit
  can be ambiguous;
- preserve schema version 1 and every current table, constraint, index, lock,
  and RCSI rule;
- do not select payloads, headers, or sensitive metadata.

Multi-host renewal/settlement races are decided atomically by SQL Server. A
renewal must never overwrite completion, retry, dead-letter, replay, retention,
or a newer lease.

## Registration and dependency behavior

- Add no DI service type or alias.
- The existing concrete SQL-file/T-SQL singleton remains the exact
  `IDurableOutputStore`, `IDurableOutputDeliveryStore`,
  `IDurableOutputDeadLetterStore`, `IDurableOutputStatusStore`, and
  `IDurableOutputRetentionStore` instance.
- Existing registration idempotency, normalized-equivalent options,
  conflict/tamper rejection, singleton lifetimes, disposal, and I/O-free
  registration/resolution remain unchanged.
- The dispatcher continues to require exactly one delivery store and one
  handler.
- Add no runtime or test package dependency.

## Required tests

Use the existing xUnit/Shouldly conventions and existing test projects. Do not
create another test project or test framework. Generate tests through the
mandatory independent testing pipeline, record `.testagent/research.md`,
`.testagent/plan.md`, and `.testagent/status.md`, and independently inspect the
generated tests before accepting them.

### Contract and option tests

Prove:

- exact immutable/sealed renewal-record surface and record equality;
- key/token guards;
- exact offset retention;
- `LeaseUntil` strictly later than `RenewedAt`, including equal-instant values
  expressed with different offsets;
- exact `IDurableOutputDeliveryStore.RenewLeaseAsync` signature;
- exact option defaults;
- positive-duration guards;
- renewal interval one tick below the lease duration is valid;
- equal or greater renewal intervals are rejected;
- the builder creates an immutable snapshot and later builder mutation cannot
  change registered options;
- the old four-argument public constructor is absent from the exported surface.

### Dispatcher tests

Use `FakeTimeProvider` and causally controlled tasks—no `Thread.Sleep`, real
time, arbitrary scheduling delay, or unowned task.

Prove:

- empty store preserves the existing idle behavior;
- a handler completing before the first interval causes zero renewals and one
  exact completion;
- a long handler receives multiple renewals at exact intervals with the exact
  key/token/current time/new expiry;
- every successful renewal retains the same handler invocation, lease token,
  owner, attempt, and envelope;
- handler completion winning the tick race avoids the renewal call;
- successful handler completion after renewal settles once;
- handler failure after renewal preserves retry timing;
- final-attempt handler failure after renewal preserves dead-letter behavior;
- each non-applied renewal status cancels the handler and produces no stale
  settlement;
- renewal result with the wrong key is rejected through the sanitized store
  exception boundary;
- renewal-store exception cancels and observes the handler, performs no
  settlement, and remains visible to the outer dispatcher;
- host cancellation cancels and observes the handler without settlement;
- handler cancellation caused by ownership loss is not misclassified as a
  retryable handler failure;
- no second handler starts while the first is renewing.

### Reusable provider conformance

Extend the existing delivery-store conformance suite so every provider proves:

- current exact token renews to the exact requested expiry;
- renewal may shorten or extend the current unexpired lease;
- renewal changes no envelope, owner, token, attempt, state, retry time, or
  dead-letter generation;
- successful renewal keeps the same token settleable and prevents reclaim
  before the new exact expiry;
- wrong token returns `LeaseLost` without mutation;
- equality at expiry is lost; one tick before expiry remains renewable;
- missing key is `NotFound`;
- pending, completed, and dead-letter states are `InvalidState`;
- repeated renewal uses current persisted expiry rules;
- pre-cancellation performs no mutation;
- renewal racing completion/retry/dead-letter has one valid winner and cannot
  revive or overwrite the winning state.

### Provider-specific tests

SQL-file tests must prove:

- exact expiry persists after reopening through a second store instance;
- renewal performs no payload/header deserialization;
- every non-expiry delivery column remains exact;
- schema versions and exact schema shape remain unchanged;
- a held external write lock causes bounded failure, preserves all state, and
  the same store can renew successfully after release;
- disposed-store and first-operation cancellation behavior remain correct.

Real T-SQL tests must prove:

- exact expiry persistence across provider instances;
- version-1 schema remains exact with no renewal-specific object;
- renewal versus completion/retry/dead-letter and competing re-lease races are
  atomic across independent store instances;
- renewal ignores corrupt payload content because it never hydrates envelopes;
- command cancellation/disposal behavior remains correct;
- the complete real-server runner finishes with zero skipped tests and cleans
  its owned container.

### Assertion quality

Every test must assert observable outcomes and neighboring state, not merely a
non-null result or the value just written. Boundary tests must distinguish
`<` from `<=`; race tests must prove the complete valid final-state set and
forbid revival/overwrite. Run pseudo-mutation and assertion-quality reviews and
record concrete killed/survived reasoning. Do not skip or weaken tests to make
the suite pass.

## Documentation requirements

Add `docs/37-durable-output-lease-renewal.md` and include it in documentation
navigation.

Update all relevant surfaces:

- root README;
- documentation index and public API overview;
- reliable in-process delivery boundary;
- durable output capture, SQL-file provider, delivery, dead-letter, T-SQL
  provider, status, and retention documentation where lease behavior matters;
- the three durable-output package READMEs;
- changelog and package release notes;
- flat registration examples;
- goal completion evidence;
- memory index, current state, architecture decisions, progress log, and a new
  `memory/284-durable-output-lease-renewal.md`.

Documentation must state:

- why renewal is needed for handlers that may outlive the initial lease;
- exact defaults and validation;
- short handlers incur no renewal call;
- the store transition and dispatcher race semantics;
- cancellation cooperation required from handlers;
- lease loss stops stale settlement but cannot undo a side effect;
- renewal reduces avoidable overlap but delivery remains at-least-once;
- destination idempotency remains mandatory;
- no schema migration or extra service alias exists;
- capture-only hosts remain unaffected.

Correct stale current-state text that still says output delivery has no
retention/purge capability and update the current-state timestamp/context.

## Versioning and compatibility

This round intentionally changes an existing provider contract and removes the
old options-constructor signature. Use semantic major versions:

- `FluxFlow.Engine.DurableOutput`: `2.2.0` -> `3.0.0`;
- `FluxFlow.Engine.DurableOutput.SqlFile`: `2.2.0` -> `3.0.0`;
- `FluxFlow.Engine.DurableOutput.TSql`: `1.2.0` -> `2.0.0`.

Durable-input package versions remain unchanged. Rename/update the six-package
durability version guard so its name and assertions are no longer tied only to
the previous retention round.

Release notes and changelog must call out:

- the cohesive `RenewLeaseAsync(...)` addition;
- the required `LeaseRenewalInterval` option;
- the removed four-argument options constructor;
- the supported-provider updates;
- custom delivery providers and direct options constructors must be updated;
- no schema or dependency change.

Run the public API baseline in check mode first. Accept only the intended
renewal record, delivery-store member, option property, builder property,
constructor replacement, and version-manifest changes, then rerun check mode.

Run package preparation, archive inspection, isolated-feed consumer tests on
both supported target frameworks, and binary compatibility. The major-version
breaks are expected and must be recorded explicitly rather than hidden. If a
published predecessor package is unavailable from configured feeds, report the
comparison as unavailable, not passed.

## Dependency and security hygiene

- Add no runtime or test dependency.
- Add no ORM, mapper, retry framework, scheduler, or generic repository.
- Keep direct SQL in existing provider delivery partials.
- Do not automatically retry state-changing renewal commands after ambiguous
  transport failure.
- Keep connection strings, payloads, headers, tokens, and sensitive metadata
  out of logs, exceptions, docs, and test output.
- Run dependency-policy and vulnerability gates for all three affected package
  lines.

## Explicit non-goals

Do not implement any of the following in this round:

- parallel or batched output delivery;
- multiple handlers/destinations or transport routing;
- exponential, variable, jittered, or pluggable retry policy;
- automatic dead-letter replay;
- automatic retention scheduling;
- durable workflow state, checkpoints, or resume-from-node;
- exactly-once processing or side effects;
- business-state/outbox atomic transactions;
- distributed transactions or distributed locks;
- handler termination or external-side-effect revocation guarantees;
- an additional hosted service, timer service, queue, or task supervisor;
- a separate renewal interface or DI alias;
- new application options, workflow JSON, component metadata, or Fluent DSL;
- provider discovery, reflection, convention scanning, or service locators;
- schema migrations, tables, columns, indexes, triggers, or stored procedures;
- new database/provider packages;
- health-check, metrics, dashboard, endpoint, CLI, or administration UI;
- changes to durable input;
- changes to FileSystem/SQL storage registration, MQTT lifecycle, or session
  helpers.

## Implementation sequence

1. Save this complete goal before production or test-source edits.
2. Run the required one-time pre-edit source/test pairing inventory and record
   its exact counts.
3. Add the immutable renewal request and cohesive delivery-store member.
4. Replace the immutable option constructor and extend the flat builder.
5. Implement the serial dispatcher renewal loop and cancellation ownership.
6. Add SQL-file renewal using its current transaction/status helpers.
7. Add T-SQL renewal using its current transaction/status helpers.
8. Update every current production and test implementation of the delivery
   store so the solution compiles without compatibility shims.
9. Generate tests through the independent test pipeline; then review and
   refine them against this goal.
10. Add contract, dispatcher, conformance, provider, persistence, concurrency,
    cancellation, and real-server evidence.
11. Update versions, release notes, changelog, public API baseline,
    documentation, examples, goal evidence, and memory.
12. Run focused builds/tests, the complete real T-SQL runner, full solution
    tests/build, public API, format, dependency, vulnerability, package,
    consumer, and compatibility gates.
13. Record exact commands, counts, skips, warnings, artifacts, container
    cleanup, and honest environmental limitations in this goal.

## Acceptance criteria

The goal is complete only when all of the following are true:

- The public renewal record and cohesive store method exist with exact
  validation and transition semantics.
- The old four-argument options constructor is removed and the flat builder
  exposes a required valid renewal interval.
- Short handlers perform no renewal I/O.
- Long handlers retain the same lease through periodic exact-token renewal.
- Non-applied renewal cancels/observes the handler and causes no stale
  settlement.
- Renewal-store failure and host cancellation leave recovery to lease expiry
  without an abandoned task.
- Existing completion, retry, maximum-attempt dead-letter, ordering, and serial
  behavior are preserved.
- SQL-file and T-SQL update only the existing expiry fields with no schema
  change or payload hydration.
- Provider conformance and real multi-instance race tests prove atomic final
  states.
- No new interface alias, worker, queue, dependency, reflection, ORM, schema,
  DSL, or application option is introduced.
- Documentation clearly retains the at-least-once and destination-idempotency
  limits.
- Major versions, release notes, changelog, public API baseline, docs, goal,
  and memory are current.
- Focused and full solution tests/builds pass without warnings.
- The real T-SQL runner passes with zero skips and leaves no owned container.
- Public API, formatting, dependency, vulnerability, package, and consumer
  gates pass; compatibility breaks/unavailable artifacts are reported
  honestly.

## Verification matrix

Record final evidence for:

| Gate | Required evidence |
|---|---|
| Pre-edit pairing | command, exact source/test/paired/unpaired counts, caveat |
| Contract/options | command, target frameworks, passed/failed/skipped |
| Dispatcher | exact renewal/cancellation/race test names and results |
| Provider conformance | SQL-file and T-SQL adapters running the same suite |
| SQL-file specifics | persistence, schema, lock recovery, payload isolation |
| T-SQL fast tests | both target frameworks and exact counts |
| Real T-SQL | full runner, digest, passed/failed/skipped, cleanup |
| Registration | unchanged aliases, idempotency, tamper/conflict, I/O-free |
| Full solution | exact command, projects, passed/failed/skipped/warnings |
| Public API | initial check, reviewed additions/removals, accept, final check |
| Versions/release | exact three new versions and six-line guard result |
| Packages | preflight, archive/symbol, feed, net8/net10 consumers |
| Binary compatibility | expected major breaks or unavailable baselines |
| Dependency/vulnerability | commands and results |
| Formatting/whitespace | commands and results |
| Release build | command, project count, errors, warnings |
| Test quality | requirement-to-test map, pseudo-mutation, assertion audit |

## Completion evidence

### Inventory and implementation

- The mandatory pre-edit pairing analyzer ran exactly once: 1,066 C# files,
  759 source files, 307 test files, 528 paired sources, and 231 statically
  unpaired sources in 7,282 ms. This was a discovery heuristic, not coverage.
- The implementation added one immutable renewal request and one direct method
  to the existing cohesive delivery-store contract. The options constructor is
  now the explicit five-argument form and the flat builder carries the same
  required interval. No compatibility overload, new alias, reflection,
  service locator, worker, queue, ORM, dependency, or schema object was added.
- The serial dispatcher uses its injected clock and one `PeriodicTimer` while
  one handler runs. Short handlers make no renewal call. Loss, renewal failure,
  and host cancellation cancel and observe the linked handler and perform no
  stale settlement.
- SQL-file and T-SQL use one guarded direct update of the existing expiry
  fields. The complete real-server test found one cancellation-boundary defect:
  a canceled blocked SqlClient command surfaced as `SqlException`. The shared
  T-SQL transition boundary now converts that case to
  `OperationCanceledException`; transaction disposal retains rollback and the
  unchanged test passes.

### Tests and builds

- Independent generated-test evidence passed: core durable output 162/162,
  SQL-file durable output 166/166, and fast T-SQL 136/136 across `net8.0` and
  `net10.0`, with zero failures or skips. The integration Release build covered
  eight projects with zero errors or warnings.
- The full real T-SQL runner passed 117/117 with zero failures and zero skips in
  8 minutes 2 seconds. It used
  `mcr.microsoft.com/mssql/server:2022-latest` at digest
  `sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`
  and left no container with the owned test prefix.
- The exact six-case durability version guard passed. Release tests excluding
  the intentionally slow documentation samples passed 114/114; the three
  documentation samples passed 3/3 in their isolated class run. The previously
  observed unrelated MQTT controller, session helper, and MQTT adapter cases
  also passed unchanged in isolation (1/1, 1/1, and 2/2).
- A serialized, no-incremental Release solution build covered 133 build targets
  with zero errors and zero warnings.
- A single all-solution test command could not be claimed as green on this
  concurrently loaded workstation. Two broad runs reached 2,452/2,453 with
  only the slow documentation sample timing out; another reached 2,449/2,453
  with four unrelated load-sensitive failures, all of which passed unchanged
  in isolation. A final run excluding the three documentation samples exceeded
  its 30-minute command envelope while the unchanged
  `FluxFlow.Resilience.Tests` project was active; that project passes 11/11 in
  isolation. The exact owned process tree was removed afterward. These are
  recorded as aggregate-environment limitations, not converted into a full-run
  pass; all changed-feature, real-server, release, and individually observed
  regression failures are green.

### API, release, package, and policy gates

- Public API check mode first detected the intentional changes. After review,
  the baseline was accepted and final check mode passed 2/2. The reviewed
  surface consists only of the immutable renewal record, delivery-store member,
  replacement options constructor/property, builder property, and the existing
  providers' direct public implementations.
- Package versions are `FluxFlow.Engine.DurableOutput` 3.0.0,
  `FluxFlow.Engine.DurableOutput.SqlFile` 3.0.0, and
  `FluxFlow.Engine.DurableOutput.TSql` 2.0.0. Release notes, changelog, package
  descriptions/tags, documentation, examples, goal, and memory were updated.
- All three release preflights passed. Fresh packages and symbols were created,
  their archives inspected, and isolated fresh-cache feed/consumer dry-runs
  passed on `net8.0` and `net10.0` for all three packages. An initial core dry
  run read stale artifacts from the old local feed and failed its consumer
  reflection check; rebuilding the current dependency-ordered local feed and
  rerunning from fresh caches passed every package. The stale attempt is not
  treated as product evidence.
- The core 2.2.0 compatibility baseline was available. SDK comparison reported
  exactly the intended major breaks: the added delivery-store interface member
  and removed four-argument options constructor. SQL-file 2.2.0 and T-SQL 1.2.0
  baselines were unavailable from configured feeds and are reported as
  unavailable, not passing comparisons. No compatibility suppression was added.
- Vulnerability checks reported no vulnerable direct or transitive package for
  any affected package. No package dependency changed. Formatting verification
  passed for all eight touched production/test project scopes, `git diff
  --check` passed, the touched-scope trailing-whitespace scan found zero hits,
  and the local documentation-link scan found zero missing targets. The
  solution-wide formatter itself was not used as evidence because it exceeded
  its envelope under the same external workspace load; only its owned format
  processes were removed.

### Test-quality conclusion

- The frozen requirement-to-test map is recorded in `.testagent/status.md`.
  It covers record shape, validation, exact option boundaries, short/long and
  tick-boundary dispatcher behavior, every renewal status, cancellation and
  observation, SQL-file row/schema/lock behavior, real T-SQL persistence/schema
  and races, registration shape, and exact versions.
- Pseudo-mutation review kills time-boundary, key/token/state predicate,
  timer-ordering, stale-settlement, persistence, transaction, schema, alias,
  and version mutations. The focused assertion audit found no assertion-free
  renewal test, swallowed exception, approximate-time assertion, sleep-based
  synchronization, or detached dispatcher task. No requirement-level gap
  remains in the implemented scope.
