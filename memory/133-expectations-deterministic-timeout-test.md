# Expectations Deterministic Timeout Test

Date: 2026-06-12

## Decision

Eliminate the flaky `Expect_TimesOutWhenMatchIsNotObserved` test race observed
during the `components-metrics-v1.2.0` publish run.

## Cause

The test sent an event and immediately completed the recorded timeout delay.
`SendAsync` only guarantees the event is queued into the node's single-threaded
`ActionBlock`; on a loaded runner the timeout task could acquire `_stateLock`
and resolve the expectation before `ProcessEvent` recorded the event, so the
result's `ObservedEvents` snapshot was empty.

## Fix

- `EventExpectationNode` gains an additive public `ObservedEventCount`
  property (lock-guarded read of the observation list) so hosts and tests can
  observe recorded event counts deterministically.
- The test now waits for `ObservedEventCount == 1` before completing the
  timeout delay. Runtime behavior is unchanged.

## Release

`FluxFlow.Components.Expectations` `1.2.0` (additive public API).

## Verification

- Expectations test project run 15 consecutive times in Release: 8/8 passed
  every iteration.
- Full Release build and release guard tests green before merge.
