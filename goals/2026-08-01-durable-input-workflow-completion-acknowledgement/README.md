# GOAL: Add explicit workflow-completion acknowledgement to durable input

## Status

In progress as of 2026-08-01. This file is the authoritative implementation
prompt and permanent engineering record for this round. It must exist before
production source changes begin.

## Executive intent

Add an explicit, opt-in durable-input acknowledgement mode that can retain and
renew the exact persisted lease until a host-defined workflow completion
boundary reports success. Preserve the existing lightweight behavior in which
an input is marked delivered as soon as the Engine accepts it.

The new mode must be provider-neutral, lease-safe, restart-safe in the same
at-least-once sense as the existing durable-input subsystem, easy to configure,
and honest about its guarantees. It must not infer workflow completion from an
empty queue, arbitrary output, elapsed time, graph traversal, reflection,
headers, naming conventions, or any other hidden mechanism. A host must
explicitly implement and register the completion boundary appropriate to its
workflow.

This round must preserve all existing features. It may add focused public
contracts and provider capabilities, but it must not make ordinary in-process
workflows or the default durable-input path pay for functionality they did not
request.

## Product decision

Durable input has two acknowledgement modes:

1. `EngineAccepted` remains the default and preserves current behavior. The
   dispatcher marks an input delivered immediately after the addressed Engine
   input accepts the restored message.
2. `WorkflowCompleted` is explicit and opt-in. The dispatcher subscribes to one
   host-defined completion source before sending the restored message, waits for
   that exact leased attempt to finish, renews its lease while waiting, and marks
   it delivered only after explicit success.

`WorkflowCompleted` means only that the registered completion source reported
success for the exact leased attempt. It does not mean exactly-once execution,
durable workflow checkpoints, transactional side effects, distributed
transactions, or automatic rollback. A process can fail after workflow side
effects and before the delivered transition commits, so workflow handlers and
external effects must remain idempotent.

## Mandatory engineering principles

1. Preserve KISS, SRP, IoC, explicit dependencies, immutable runtime options,
   and small feature-local types.
2. Keep configuration flat through the existing
   `Action<DurableInputOptionsBuilder>` callback. Do not add nested callbacks or
   an `IOptions<T>` graph.
3. Keep the default `EngineAccepted` path behaviorally compatible and free of
   completion subscriptions, renewal operations, timers, or additional
   provider requirements.
4. Keep `IDurableInputStore` unchanged. Lease renewal is an additive optional
   capability so existing providers continue supporting `EngineAccepted`
   without source changes.
5. Use ordinary dependency injection for the host completion source and store
   capabilities. Do not use reflection, assembly scanning, dynamic invocation,
   service-name conventions, or hidden graph inspection.
6. Subscribe before sending so a fast workflow cannot complete between Engine
   acceptance and completion observation.
7. Pass the exact `DurableInputLease`, including its key, attempt, and lease
   token, to the completion source. The source must be able to distinguish late
   completion from an older attempt; key-only or trace-only matching is not a
   sufficient general guarantee.
8. Renew only the current, unexpired lease through an atomic compare-and-set
   operation. A lost, expired, missing, or invalid lease must never be revived.
9. Do not add an ORM, generic repository, broad persistence abstraction,
   external scheduler, new background service, or new third-party package.
10. Keep provider settings out of `FluxFlowApplicationOptions`.
11. Preserve stable, non-sensitive logs. Do not log payloads, headers, completion
    details supplied by external systems, or storage connection settings.
12. Do not silently claim stronger delivery semantics than the implementation
    provides.

## Public API design

### Acknowledgement mode

Add the provider-neutral enum:

```csharp
public enum DurableInputAcknowledgementMode
{
    EngineAccepted = 0,
    WorkflowCompleted = 1
}
```

Zero must remain the current/default behavior. Undefined enum values are
invalid.

### Immutable options and flat builder

Extend `DurableInputOptions` and `DurableInputOptionsBuilder` with:

- `AcknowledgementMode`, default `EngineAccepted`;
- `WorkflowCompletionTimeout`, default five minutes;
- `LeaseRenewalInterval`, default ten seconds.

Preserve the existing six-argument `DurableInputOptions` constructor so source
and binary callers can continue constructing the current behavior. Add one
explicit full constructor for all settings. Do not add init setters to the
resolved options; it remains an immutable sealed record with get-only
properties.

Validation rules:

- acknowledgement mode must be defined;
- workflow-completion timeout must be positive or
  `Timeout.InfiniteTimeSpan`;
- lease-renewal interval must be positive;
- in `WorkflowCompleted` mode the renewal interval must be strictly shorter
  than the lease duration;
- existing option validation remains unchanged;
- invalid configuration must fail while the builder is resolved, before
  `IServiceCollection` mutation.

The existing registration shape remains canonical:

```csharp
services.AddFluxFlowDurableInput(options =>
{
    options.AcknowledgementMode = DurableInputAcknowledgementMode.WorkflowCompleted;
    options.WorkflowCompletionTimeout = TimeSpan.FromMinutes(10);
    options.LeaseDuration = TimeSpan.FromSeconds(30);
    options.LeaseRenewalInterval = TimeSpan.FromSeconds(10);
});

services.AddSingleton<IDurableInputCompletionSource, OrderWorkflowCompletionSource>();
```

No additional callback level is permitted.

### Exact lease-renewal capability

Add an immutable `DurableInputLeaseRenewal` request. Its constructor accepts the
durable-input key, exact lease token, renewal time, and new lease expiry. It must
validate the key/token and require the new expiry to be later than the renewal
time.

Add the optional provider capability:

```csharp
public interface IDurableInputLeaseRenewalStore
{
    ValueTask<DurableInputTransitionResult> RenewLeaseAsync(
        DurableInputLeaseRenewal renewal,
        CancellationToken cancellationToken = default);
}
```

The capability contract requires an atomic update only when the row exists, is
currently leased, has the exact lease token, and is unexpired at the renewal
time. It changes only the lease expiry. It must not change the attempt, owner,
payload, state, next-attempt time, delivered time, failure, or dead-letter
generation. Non-applicable renewals return the existing transition statuses and
must never recreate or revive a lease.

Do not add `RenewLeaseAsync` to `IDurableInputStore`; existing providers remain
valid for the default mode.

### Explicit completion boundary

Add a minimal host extension point:

```csharp
public interface IDurableInputCompletionSource
{
    ValueTask<IDurableInputCompletionSubscription> SubscribeAsync(
        DurableInputLease lease,
        CancellationToken cancellationToken = default);
}

public interface IDurableInputCompletionSubscription : IAsyncDisposable
{
    Task<DurableInputCompletionResult> Completion { get; }
}
```

The source receives the exact lease before Engine dispatch. A correct source
must bind its explicit terminal business/workflow signal to that attempt and
must reject or ignore completion from an older token. It owns domain-specific
observation and correlation; FluxFlow must not guess it.

Add a small immutable `DurableInputCompletionResult` with:

- one shared successful result or success factory;
- one explicit failed-result factory accepting a trimmed, non-empty stable
  description;
- `IsCompleted` and optional failure-description accessors;
- no mutable properties and no exception-based expected control flow.

The description is persisted as the existing retry failure description. API
documentation must tell completion-source authors not to include secrets,
payloads, or unstable exception text.

Add failure kinds after the existing numeric values without renumbering them:

- `CompletionSourceUnavailable`;
- `WorkflowCompletionFailed`;
- `WorkflowCompletionTimedOut`.

## Dependency-injection behavior

`AddFluxFlowDurableInput` retains equivalent-repeat idempotency and
different-options conflict detection.

In `EngineAccepted` mode:

- no completion source is required;
- no renewal store is required;
- registered optional capabilities are not called;
- the dispatcher leases up to the configured `BatchSize` as before.

In `WorkflowCompleted` mode:

- exactly one `IDurableInputCompletionSource` must be resolvable;
- exactly one `IDurableInputLeaseRenewalStore` must be resolvable;
- missing or multiple capabilities fail with clear actionable errors when the
  dispatcher is composed, before message processing starts;
- do not silently fall back to `EngineAccepted`;
- one message is leased at a time. This avoids leasing a batch whose later
  leases expire while a prior workflow is still running. `BatchSize` continues
  to control only the acceptance mode in this round; completion-mode
  concurrency is an explicit future concern, not hidden parallelism.

Use constructor-injected capability collections or another small composition-
root mechanism; do not spread service-location calls through runtime logic.

## Dispatcher behavior

Preserve all existing validation, contract restoration, port compatibility,
retry, dead-letter, maximum-attempt, cancellation, and store-failure behavior.

For `EngineAccepted`, preserve the current sequence exactly: lease, validate
and restore, send, then mark delivered on `Accepted`.

For `WorkflowCompleted`:

1. Lease exactly one eligible input.
2. Perform schema, contract, address, direction, and payload validation before
   opening a completion subscription.
3. Subscribe using the exact lease before calling `RestoreAndSendAsync`.
4. If subscription setup fails or returns an invalid subscription/completion
   task, log safe diagnostics and release/dead-letter through existing attempt
   policy with `CompletionSourceUnavailable`.
5. Send the restored message.
6. If send is not accepted, dispose the subscription and preserve the existing
   send-status retry behavior.
7. If accepted, wait for explicit completion while renewing the lease at the
   configured interval.
8. After each interval, prefer an already-completed signal before performing a
   renewal or declaring timeout.
9. Renew to `current time + LeaseDuration` using the exact key/token.
10. If renewal returns a non-applied transition, stop waiting and do not mark,
    release, or dead-letter using the lost lease.
11. On explicit success, mark delivered with the exact current token.
12. On explicit failed result, release with `WorkflowCompletionFailed`, subject
    to the existing maximum-attempt dead-letter rule.
13. On timeout, release with `WorkflowCompletionTimedOut`, subject to the same
    attempt rule.
14. If the completion task faults, log the exception through the logger without
    persisting its message, then retry with a stable
    `WorkflowCompletionFailed` description.
15. On host cancellation/shutdown, stop and leave the lease to expire; do not
    manufacture a success or release that could race with still-running work.
16. Always dispose a created subscription. Disposal failure must be logged and
    must not reverse an already committed settlement or terminate the hosted
    dispatcher.

Use `TimeProvider` for renewal and timeout scheduling so behavior is
deterministic in tests. Avoid an independent per-message background worker;
the existing dispatcher loop owns the wait.

Store-operation exceptions remain wrapped/classified by the dispatcher so the
existing store-failure delay and expiry recovery continue to apply. Completion-
source failures are application-boundary failures, not storage failures.

## SQL-file provider

Make `SqlFileDurableInputStore` implement
`IDurableInputLeaseRenewalStore` in addition to its current interfaces.

Required SQL behavior:

- use one parameterized `UPDATE` inside the existing explicit write-transaction
  pattern;
- match application address, message id, leased state, exact lease token, and
  `lease_until_utc_ticks > renewedAt`;
- set only `lease_until_utc_ticks` to the requested expiry;
- resolve zero affected rows through the established transition-status logic;
- commit non-cancelably only after the caller-visible cancellation checkpoint;
- preserve busy/locked exception behavior and safe messages;
- add no table, column, index, or schema migration because the current
  version-2 schema already stores lease expiry and token.

`AddFluxFlowSqlFileDurableInput` must expose the renewal interface as an alias
to the exact same concrete singleton. First registration must reject a
pre-existing renewal-capability registration before mutation. Equivalent repeat
registration remains idempotent. Service-provider construction and store
resolution remain free of database I/O.

## Versioning and release governance

This is an additive public capability:

- advance `FluxFlow.Engine.DurableInput` from `1.0.0` to `1.1.0`;
- advance `FluxFlow.Engine.DurableInput.SqlFile` from `1.0.0` to `1.1.0` so its
  package advertises and depends on the renewal-capable core line;
- update package release notes and changelog entries;
- update the source-declaration public API baseline only after reviewing the
  exact intended declarations;
- keep package aliases and tag prefixes unchanged;
- pack both target frameworks and inspect dependency/version metadata;
- run release and binary-compatibility preflight according to repository
  policy, recording any unavailable prior public baseline as an explicit
  environment caveat rather than hiding it.

## Required tests

### Provider-neutral contract and dispatcher tests

Add focused tests for:

- acknowledgement enum validity and stable numeric values;
- old options constructor preserving `EngineAccepted` defaults;
- full immutable options constructor and builder mappings;
- default completion timeout and renewal interval;
- invalid enum, non-positive timeout, allowed infinite timeout, non-positive
  renewal interval, and renewal interval not shorter than lease duration in
  completion mode;
- existing builder validation and equivalent/different registration behavior;
- renewal-request key, token, and time invariants;
- completion-result success/failure invariants and immutability;
- missing and duplicate completion-source diagnostics;
- missing and duplicate renewal-store diagnostics;
- default mode requiring and invoking neither optional capability;
- completion mode leasing one input even when `BatchSize` is larger;
- subscription established before send;
- accepted input not marked delivered before explicit completion;
- explicit success marks delivered;
- explicit failure retries and reaches maximum-attempt dead-letter behavior;
- subscription setup failure, null/invalid return, completion task fault, and
  completion timeout;
- lease renewal while waiting and renewal expiry calculated from the injected
  clock;
- renewal loss preventing later settlement;
- send rejection disposing the subscription and preserving old retry failure;
- cancellation leaving the lease unsettled;
- subscription disposal on all created-subscription paths;
- disposal failure being logged without undoing settlement or stopping later
  dispatch;
- safe logging with no payload or host failure-detail leakage where applicable.

Use deterministic fakes and `TimeProvider`; do not add real sleeps.

### Store conformance and SQL-file tests

Add a reusable renewal-capability conformance suite covering exact current-token
renewal, wrong-token rejection, expired-lease rejection, non-leased state
rejection, missing-key results, unchanged attempt/state/content, cancellation,
and deterministic renewal-versus-settlement races. Run it against the SQL-file
provider.

Add SQL-file registration/persistence coverage for exact singleton aliasing,
equivalent repeat idempotency, atomic conflict failure on a pre-existing renewal
store, expiry persistence across reopen, stale/expired rejection, race safety,
and unchanged schema version 2.

Extend the in-memory test store only as needed for dispatcher tests; do not turn
it into production code.

## Mandatory test-quality workflow

Before creating or editing test source:

1. Send the complete testing task to the existing `code_testing_generator`
   agent.
2. Run `find-untested-sources` exactly once for the affected production/test
   scope and retain its JSON evidence.
3. Update `.testagent/research.md`, `.testagent/plan.md`, and status evidence for
   this round.
4. Run test-gap analysis and implement meaningful public behavior, boundary,
   failure, integration, cancellation, and concurrency coverage.
5. After tests pass, perform the required pseudo-mutation review and assertion-
   quality audit, including the .NET assertion extension guidance.
6. Remedy real gaps and report exclusions explicitly.

The final handoff must include a compact `Requirement | Evidence` table naming
the exact test classes/artifacts that prove each major requirement.

## Documentation and memory

Update every affected surface:

- root `README.md` package/capability guidance;
- both durable-input package READMEs;
- `docs/README.md` navigation;
- public API, runtime architecture, reliable-delivery, durable-input, and
  SQL-file durable-input documentation;
- a focused new documentation page for workflow-completion acknowledgement;
- `CHANGELOG.md`;
- memory index, current state, architecture decisions, progress log, and a new
  numbered memory record;
- `goals/README.md` only if its established convention indexes individual
  goals (currently it documents the convention rather than listing them).

Documentation must show minimal default registration, full flat opt-in
registration, ordinary DI registration of one completion source, and a concise
source example that uses the exact lease token and subscribes before dispatch.
It must explain Engine acceptance versus explicit completion; non-inference;
timeout, retry, renewal loss, shutdown, and maximum-attempt behavior; provider
capability requirements; SQL-file support without migration; continuing
idempotency; and explicit non-claims around exactly-once, checkpoints, and
distributed transactions.

## Required validation sequence

1. Format-check all touched C# source.
2. Build and test the durable-input core for `net8.0` and `net10.0`.
3. Build and test the SQL-file provider for both target frameworks.
4. Run the mandatory test-quality workflow and remedy meaningful findings.
5. Run focused package, documentation-boundary, and public-API tests.
6. Accept the reviewed public API baseline through the repository mechanism and
   rerun its test without the acceptance variable.
7. Build the complete solution in Debug and Release with zero warnings.
8. Run the complete default Release test suite with zero failures.
9. Pack both updated packages and inspect contents/dependencies.
10. Run package release/binary-compatibility preflight for both aliases.
11. Search touched source and package output for reflection, ORM dependencies,
    generic repositories, nested configuration callbacks, leaked secrets,
    accidental schema changes, or dependency propagation.
12. Record exact build/test/package counts and any environment caveats in memory.

## Explicit non-goals

- No exactly-once execution or delivery claim.
- No durable workflow checkpoint or resume engine.
- No distributed transaction coordinator or transaction coupling between the
  workflow and inbox store.
- No inference of completion from queue emptiness, arbitrary output, graph
  topology, trace identifiers alone, timers, reflection, or naming convention.
- No built-in domain completion event or universal completion component.
- No parallel workflow-completion dispatcher in this round; process one active
  leased workflow at a time.
- No change to ordinary Engine ports, messages, graph semantics, or the C# DSL.
- No new persistence provider, ORM, migrations framework, generic repository,
  service factory, health dashboard, pruning policy, or telemetry exporter.
- No SQL-file schema migration.
- No provider settings in application-level options.
- No breaking change to `IDurableInputStore`.

## Completion criteria

The goal is complete only when:

- this prompt exists before production source changes and accurately describes
  the delivered behavior;
- default `EngineAccepted` behavior remains unchanged and lightweight;
- `WorkflowCompleted` requires one explicit completion source and one renewal
  capability, with no silent fallback;
- subscription precedes send, exact leases renew safely, and only explicit
  success causes delivery;
- failures, timeout, lease loss, cancellation, disposal, retry, and dead-letter
  paths are deterministic and documented;
- the SQL-file provider renews leases atomically without a schema change;
- options and runtime records remain immutable and registration remains flat;
- no feature is removed and no prohibited magic/dependency is introduced;
- docs, navigation, changelog, API baseline, package metadata, and memory are
  current;
- focused and full Debug/Release validation pass with zero warnings/failures;
- packages build and governance checks pass or an external feed/environment
  limitation is recorded precisely;
- the final report maps requirements to exact evidence.
