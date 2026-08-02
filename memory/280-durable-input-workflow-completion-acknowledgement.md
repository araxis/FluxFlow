# Durable Input Workflow-Completion Acknowledgement

Date: 2026-08-01

## Outcome

FluxFlow durable input now has two explicit acknowledgement boundaries.
`EngineAccepted` remains the default and preserves the existing lightweight
batched dispatcher. `WorkflowCompleted` is an opt-in host contract: the host
supplies exactly one completion source, the provider supplies exactly one lease
renewal capability, and the dispatcher settles only an explicit completion
result for the exact leased entry.

The implementation deliberately does not inspect workflow definitions, infer
terminal nodes, watch outputs or traces, or treat elapsed quiet time as success.
It adds no reflection, ORM, general orchestration layer, hidden queue, or Engine
dependency.

## Public contract

- `DurableInputAcknowledgementMode` selects `EngineAccepted` or
  `WorkflowCompleted`.
- The existing six-argument immutable options constructor is preserved and
  resolves to the original behavior. A full constructor and the existing flat
  builder expose completion timeout and renewal interval.
- `IDurableInputCompletionSource` creates one exact-lease subscription before
  Engine dispatch. The disposable subscription exposes one completion task
  yielding explicit success or a stable failure description.
- `IDurableInputLeaseRenewalStore` is additive and leaves
  `IDurableInputStore` unchanged. Renewal uses the entry key, lease token,
  renewal time, and exact requested expiry.
- Workflow mode validates exactly one completion source and renewal capability,
  then leases at most one entry. Default mode ignores optional capabilities and
  retains configured batching.

## Settlement semantics

Invalid envelopes, unknown contracts, and invalid ports fail before completion
subscription. The dispatcher subscribes before calling Engine so an immediate
completion cannot be missed. Engine rejection follows the existing retry or
dead-letter policy. Engine acceptance in default mode marks delivered
immediately; in workflow mode it waits for explicit completion, renews the
current lease at the configured interval, and then marks delivered only on
success.

Completion setup errors, task faults, independent cancellation, explicit
failure, and timeout become stable transition failures. A lost or otherwise
non-applied renewal stops local ownership without making a stale settlement.
Host cancellation leaves the lease for ordinary expiry recovery. Subscription
disposal always runs and cannot undo an already committed transition. Logs
contain stable metadata and exception types, not payloads, completion details,
or secrets.

## SQL-file provider

`SqlFileDurableInputStore` implements the renewal capability through the same
DI-owned singleton already used for delivery and dead-letter operations. One
transactional parameterized compare-and-set update requires leased state, the
exact key and token, and a strictly unexpired lease. It changes only
`lease_until_utc_ticks` to the exact requested value. The durable-input schema
remains version 2; no migration or new dependency is needed.

## Reliability boundary

Both acknowledgement modes remain at-least-once. Workflow-completion mode
narrows the success boundary, but a crash after external side effects or after
completion and before store settlement may redeliver the input. FluxFlow does
not claim durable internal workflow execution, checkpoint/resume, exactly-once
side effects, producer-state atomicity, or distributed transactions.

## Validation

Focused provider-neutral and real-SQLite tests cover immutable option defaults
and validation, registration composition and exact dependency diagnostics,
subscribe-before-send ordering, explicit completion/failure/timeout/cancel/
fault paths, deterministic renewal cadence and lease loss, retry/dead-letter
reuse, disposal, persistence, reopen, row-level mutation, lifecycle, and race
semantics. The focused Debug and Release suites pass 117 provider-neutral and
97 real-SQLite tests. Release governance passes 111 tests; Debug and Release
builds cover 131 projects with zero warnings; and the default Release suite
passes 2,141 tests across 64 projects.

Both 1.1.0 package/symbol pairs pass archive inspection, isolated consumer
execution, feed verification, and release preflight against the matching local
release-train package source. Binary-compatibility preflight was attempted but
cannot compare either package because the 1.0.0 baseline archives are not
available from the configured feeds. No incompatibility was reported; the
comparison could not start without its external baseline artifact.
