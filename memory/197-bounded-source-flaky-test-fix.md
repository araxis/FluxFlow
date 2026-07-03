# Bounded Source Flaky Test Fix

Date: 2026-07-03

## Summary

Made the flaky bounded-source backpressure test deterministic. The test in
`196-full-icon-rollout-completion.md` that was worked around by retry
(`FluxFlow.Nodes.Tests.FlowMultiOutputAndSourceTests
.Source_EmitAsync_WaitsWhenBoundedOutputIsFull`) is now rewritten as
`Source_EmitAsync_DeliversLatestThroughBoundedOutputAndCompletes` and passes
60/60 in isolation.

## Root cause

The old test linked the source's bounded output (`OutputCapacity = 1`,
implemented as a `BroadcastBlock` with `BoundedCapacity = 1`) to an
auto-draining `BufferBlock`, then asserted `SecondAccepted.IsCompleted` was
`false` at a single instant — i.e., that the second `EmitAsync` was still
blocked. That state does not exist deterministically:

- A bounded `BroadcastBlock` is latest-wins and coalesces. It does not hold its
  slot when a target postpones — it keeps the message as the current broadcast
  value, frees its input buffer, and accepts the next message (overwriting the
  current value). Verified empirically: a pre-filled/full sink made the second
  emit complete immediately (assertion failed 40/40).
- The only window where the second `EmitAsync` postpones is the sub-millisecond
  interval while the block's internal broadcasting task lifts the first message
  out of its single-slot input buffer. That is pure internal scheduling and is
  not observable deterministically. It is exactly why the original assertion
  flaked (~2/5 locally) — and flaked in both directions once probed.

`FluxFlow.Nodes/FlowSourceOptions.cs` already documents this:
"Broadcast output remains latest-wins; this is not a durable queue or no-loss
delivery guarantee." The old test was asserting a behavior the design
deliberately does not promise.

## Fix

Rewrote the test to assert the design's actual, deterministic contract for a
bounded (latest-wins) source output:

- delivery to a keeping-up consumer stays ordered,
- the final emitted value always arrives last,
- the source completes without fault.

Intermediate values may coalesce under load (also confirmed empirically: even a
"both messages arrive in order" assertion failed 1/50, because the first value
can be overwritten before delivery), so the test tolerates coalescing:
`received` is non-empty, ordered ascending, and ends with the last emitted
value (`2`). Also simplified the `BoundedCountingSource` test helper by removing
the now-unused `FirstAccepted`/`SecondAccepted` gates.

No production source changed — this is a test-only change in
`tests/FluxFlow.Nodes.Tests/FlowMultiOutputAndSourceTests.cs`, so no
`FluxFlow.Nodes` package version bump or republish is required.

## Verification

- The rewritten test passed 60/60 consecutive isolated runs (Release, no-build).
- The old test failed ~2/5 in isolation; the two intermediate fix attempts
  (direct receive; pre-filled sink) failed 28/30 and 40/40 respectively and
  were discarded — the empirical runs are what drove the correct diagnosis.
- Full `FluxFlow.Nodes.Tests` suite passed: `36` passed, `0` failed.
- Only the known-flaky test in `133-expectations-deterministic-timeout-test.md`
  and this one have been observed flaky; both are now deterministic.
