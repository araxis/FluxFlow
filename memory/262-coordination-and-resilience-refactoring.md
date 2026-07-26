# Coordination And Resilience Refactoring

Date: 2026-07-26

## Scope

The repository-wide coordination and resilience pass is implemented on local
branch `work/coordination-resilience`. It removes duplicated workflow
request/reply, acknowledgement, timeout, and retry mechanics without creating a
generic Core package or moving protocol behavior into shared infrastructure.
No branch push, tag, package publication, pull request, or merge was performed.

## Identity Contract

- `TraceId` is the stable end-to-end workflow processing lineage and the
  default key for internal Ack/Nak, timeout, and signal coordination.
- `MessageId` identifies one emitted envelope.
- `CausationId` identifies the envelope that caused the current envelope.
- `CorrelationId` remains available for external, business, protocol, and
  compatibility contracts; it is not required by new shared coordination.
- Retry attempts use the internal composite `RetryAttemptKey(TraceId, Attempt)`
  and an instance-private attempt header. Workflow authors continue to observe
  the operation through its stable `TraceId`.

## Composition Topology

`ApplicationLinkCompiler` now classifies cycles from registered port semantics.
Ordinary message links still form the data graph and genuine data-link cycles
remain invalid. Links targeting explicit `CompositionPortKind.Signal` metadata
are bounded feedback relations and do not create data-cycle edges. This permits
Output-to-Ack/Nak/Cancel feedback with local or fully qualified addresses while
preventing an ordinary message port named Ack from bypassing cycle validation.

`FluxFlow.Composition` moved from `3.0.0` to `3.0.1` for this behavioral fix.
No public declarations changed in that package.

## Coordination Foundation

New package `FluxFlow.Coordination` `1.0.0` contains
`PendingExchangeCoordinator<TKey,TContext,TOutcome>` and explicit start,
feedback, completion, and status contracts. It provides:

- bounded actual in-flight state and deterministic duplicate/capacity/stopped
  start results
- generic keys and caller-owned contexts/outcomes without transport terms
- one `TimeProvider` timer and priority deadline queue rather than one timeout
  task or cancellation source per operation
- resolve, fault, cancel, timeout, stop, fault-all, and async disposal
- first-terminal-outcome-wins behavior under concurrent resolution, timeout,
  cancellation, stop, and disposal
- bounded recent settlement history for duplicate/late classification
- asynchronous continuations outside the coordinator lock and ordered
  settlement of accepted operations during shutdown

Keys that can represent multiple generations must include their generation in
`TKey`; Retry does so with `RetryAttemptKey`. Coordination depends only on
`FluxFlow.Nodes` and BCL abstractions.

## Compatibility Migrations

`FluxFlow.Components.RequestReply` moved to `1.2.0`.
`CorrelatedRequestTracker` is now a compatibility adapter over the shared
coordinator while retaining its established `CorrelationId` API. It adds an
actual in-flight capacity bound, preserves fire-and-forget without pending
state, and atomically settles accepted requests during shutdown. HTTP
request/reply behavior consumes this migration transitively; the Engine's
direct port observation remains Engine-local.

MQTT workflow Ack/Nak tracking now uses
`PendingExchangeCoordinator<TraceId,MqttTriggerDelivery,MqttWorkflowOutcome>`.
The trigger no longer owns a pending dictionary, timeout task, or cancellation
source per delivery. Broker acknowledgement aggregation remains MQTT-specific,
and concrete provider-session pending operations remain adapter-specific.

## Resilience Foundation And MQTT Reconnect

New BCL-only package `FluxFlow.Resilience` `1.0.0` contains retry policy,
overflow-safe fixed/linear/exponential schedules, attempt and duration budgets,
delay caps, deterministic jitter sources, pure state transitions, and an
optional direct-call executor.

`FluxFlow.Components.Mqtt` moved from `6.0.0` to `6.1.0`. Its public
`MqttRetryPolicy` remains as an adapter to shared retry policy. MQTT still owns
failure classification, reconnect suppression, reset behavior, auto-connect,
connection lifecycle, desired subscriptions, provider operations, and domain
events. Reconnect planning now uses shared delay/budget calculations and a real
injectable jitter source instead of repeatedly using the neutral midpoint
sample.

## Workflow Retry Component

New runtime package `FluxFlow.Components.Resilience` `1.0.0` exposes
`FlowRetryNode`, flat `FlowRetryOptions`, and Retry result/diagnostic contracts.
Its ports are Input, Ack, Nak, Cancel, Output, and Events.

- Input begins one logical operation per `TraceId`.
- Output emits `FlowMessage<FlowResult<RetrySignal>>` for attempt, scheduled
  retry, completion, exhaustion, cancellation, and rejection.
- Ack completes; Nak applies policy; Cancel terminates; attempt timeout retries
  or exhausts.
- Concurrent logical operations are bounded by semantic `Capacity`.
- Attempt feedback must preserve the private attempt header; late feedback from
  an older attempt is rejected.
- Expected failure is ordinary result data. There is no Error or State port.
- Events remains diagnostic data for logging, metrics, and tracing.
- Completion and disposal settle pending operations before output completion.

The waiting-cancel race discovered during verification was fixed by claiming
terminal ownership before cancelling the retry delay, ensuring explicit Cancel
cannot be reported as shutdown.

New adapter package `FluxFlow.Components.Resilience.Composition` `1.0.0`
registers canonical `flow.retry` through `RegisterFlowRetry()`. It exposes flat
option binding, fixed message/signal ports, optional host-owned clock and jitter
resources, and validated Designer metadata. The manifest now contains 62
packages and all four new source/test project pairs are registered in the
solution.

## Public API And Documentation

The source-declaration baseline was regenerated only for the four new packages
and intentional additive RequestReply/MQTT declarations. Package READMEs,
release notes, top-level changelog, public API overview, data identity guidance,
component composition guidance, type-name guidance, and coverage matrix now
document shared-versus-protocol ownership and signal-feedback semantics.

Gate remains a separate future Control-family pass. The agreed candidate is
`control.gate` with Input, Open, Close, Output, Events, and bounded
drop-oriented queue behavior; it does not use pending-exchange coordination.

## Verification

Focused tests passed with zero warnings:

- Nodes 41; Coordination 15; Resilience 11
- Retry runtime 10; Retry Composition 6
- RequestReply 26; HTTP AspNetCore 16
- Composition 111; Composition Hosting 29; Engine 55; Designer 112
- MQTT core 50; MQTT Composition 9; neutral adapter contract 7; MqttNet 8;
  PulseMqtt 6
- Release.Tests 99

Controlled Debug and Release builds each completed 137 projects with zero
errors and zero warnings.

All 62 manifest packages packed into a fresh temporary package source outside
the repository. Preflight and local-source dry-runs passed for Coordination,
Resilience, Retry runtime, Retry Composition, Composition, RequestReply, and
MQTT; each dry-run verified the archive and built a clean net8.0 package-only
consumer. SDK binary compatibility passed for Composition `3.0.1` against
`3.0.0`, RequestReply `1.2.0` against `1.1.6`, and MQTT `6.1.0` against `6.0.0`.

Graph output was refreshed after the memory update and remains excluded from
git. Final closeout includes `git diff --check`, worktree inspection, neutral
name/text scanning, and a requirement-by-requirement audit.
