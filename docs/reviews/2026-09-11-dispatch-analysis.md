# Dispatch analysis and prepared bookkeeping

Status: investigation progressed; performance and overall convergence remain open.

## Validated evidence

Four previously collected traces were analyzed offline. The analyzer validates stack
nesting, frame indexes and event ordering, then attributes exclusive sampled time to
methods after removing synthetic leaf markers. Two method percentages per trace were
cross-checked against the collector's independent method report (tolerance: 0.011
percentage points). Raw trace/export hashes and 1,480 source inputs were unchanged
at analysis verification, before the subsequent source edit below.

Evidence is under artifacts/core-refactoring/dispatch-trace:
Analyze.cs, verify-analysis.ps1, results/stack-analysis-validated.json and
results/analysis-verification.json. The initial stack-analysis.json preserves an
unsuitable attribution attempt that counted synthetic markers as methods; it is not
the accepted method analysis.

| Trace | Completed messages | Lock-method sampled seconds | Pool-wait sampled seconds |
| --- | ---: | ---: | ---: |
| Direct before | 2,135,911 | 164.835 | 116.899 |
| Direct after | 1,985,011 | 164.906 | 61.506 |
| Hosted before | 415,031 | 21.561 | 128.999 |
| Hosted after | 377,480 | 21.096 | 152.747 |

These are sampled thread durations, including waits, not processor usage or measured
lock-contention duration. Direct and hosted pairs bracket different source changes.
The direct lock-method percentage rises from 40.94% to 51.71% mostly through a changed
denominator; absolute sampled lock time is nearly unchanged. Less completed work means
the normalized value still worsens, from 77.17 to 83.08 sampled microseconds/message.
This does not dismiss the earlier uninstrumented slowdown or identify its cause.

Direct lock stacks include ordinary output admission and downstream native block
admission. Hosted stacks include diagnostic publication through a broadcast block.
Neither observation establishes a removable bottleneck. Sampled waiting dominates
large portions of these traces. Runtime allocation/collection events have not yet
been analyzed in detail. No performance gate is closed by this analysis.

## Bounded source change

FlowOutput's active-dispatch count was written on both ordinary and prepared delivery.
Its sole production reader is ApplicationOutputPort, which always configures prepared
dispatch. Ordinary delivery does not need these two volatile writes per message.

- Rename the private counter to _activePreparedDispatch and the internal count to
  PendingPreparedMessages.
- Retain prepared-delivery entry/finally writes and hosted count/drain behavior.
- Remove only the unused ordinary-delivery writes.
- Update the hosted getter and four existing test references; no assertions weakened.

There is no new queue, public API, dependency or capacity policy. This is removal of
unnecessary work, not a demonstrated throughput improvement or false-sharing fix.
Core and Engine must be rebuilt together because their internal contract changed.
Frozen earlier comparison binaries remain untouched. Fresh paired timing is pending.

Verification: all 242 Core cases across both target frameworks and all 489 Engine
cases pass (731 total, zero failed/skipped). A fresh nonincremental full Release
solution build succeeds with zero warnings/errors. Reports and build log are under
artifacts/core-refactoring/prepared-bookkeeping-tests. Broader earlier qualification
must not be represented as a fresh run of this source revision.

## Remaining physical queue audit

Current source inspection identifies these typical component-to-component paths:

```text
Ordinary:
component output queue -> target component input block

Revision-enabled:
component output queue -> prepared staging buffer -> stable output queue
  -> stable input dispatch block -> target component input block
```

FlowRouter reuses a component's canonical output when available. In the optional
runtime, FlowValuePreparedApplicationOutput creates a bounded staging buffer and
keeps it in the path after activation. ApplicationOutputPort owns a prepared canonical
output queue. ApplicationInputPortCore owns a bounded serial dispatch block and
attachment-generation policy. Sharing the fan-out implementation does not eliminate
these physical stages.

Source anchors: src/Core/FluxFlow.Core/FlowRouter.cs;
src/Engine/Runtime/FluxFlow.Engine/Ports/ApplicationOutputAttachments.cs;
src/Engine/Runtime/FluxFlow.Engine/Ports/ApplicationOutputPort.cs;
src/Engine/Runtime/FluxFlow.Engine/Ports/ApplicationInputPortCore.cs.

Next, justify each optional queue against preactivation fault isolation, ordering,
capture timing, shared capacity, generation switching and drain ownership. A bypass
must preserve these behaviors; deleting buffers or adding a nominal application
wrapper is not a substitute for that proof. Prioritize this ownership audit over
further speculative micro-optimizations. The complete feature ledger, wider load
qualification and fresh local package candidate remain open. Nothing published.
