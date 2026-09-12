# Revision queue ownership audit

Status: the three optional queue boundaries below have distinct current contracts.
Blind deletion is rejected by source evidence and behavior checks. This is not a
claim that no further redesign is possible, nor a full runtime acceptance sign-off.

## Findings and decision

| Boundary | Owner and retained policy | Why bypass is not equivalent |
| --- | --- | --- |
| Candidate staging | FlowValuePreparedApplicationOutput; bounded native buffer linked during preparation, attached to the stable output only on activation | Canonical output discards unlinked messages. Delaying its link can lose preactivation data; attaching immediately can expose uncommitted data. Abandoned candidates must never deliver into a replacement. |
| Stable output | ApplicationOutputPort owns a prepared Core FlowOutput; capture, then selected route snapshot, then shared fanout and observations | Capture must precede selection and delivery. A revision waits for capture/selection, but not for already selected target admissions. Multiple attached sources share this serialization/capacity boundary. |
| Stable input | ApplicationInputPortCore owns a bounded native serial block; attachment generation, pause, retry of rejected admission and drain | Already claimed work finishes against its target; queued work moves to the new target. Directly wiring components bypasses that distinction and stable external addresses. |

Component input blocks still perform component work. Component outputs still own
their ordinary delivery. Workflows add no scheduling stage. Optional queues have
bounded native storage/execution; they are not three copies of the reliable fanout
algorithm. Ordinary FlowRouter and hosted prepared dispatch both enter Core
FlowOutput/FlowDeliveryBatch. Application request APIs use Core FlowRequestReply.

The result is not one physical queue per hop. The real revision path is:

```text
component output -> candidate staging -> stable output
  -> stable input -> component input
```

Keep this cost visible in load accounting. Do not reduce the tracking bound by
pretending candidate staging disappears after activation. An optimization could
coalesce boundaries only after proving equivalent admission, ordering, cancellation,
rollback and drain behavior, including concurrent old/new source attachments.

## Fresh native-source evidence

NativePreparedOutputTests adds four cases in the existing Engine test project:

- Prepared_native_output_retains_order_until_activation (capacities 1 and 3): a
  full candidate batch is emitted by real canonical FlowOutput. Its dispatch barrier
  completes before the test checks that capture remains empty and the stable output
  has no active attachment. After activation, prepared drain completes and captures
  contain every original message in exact order by reference, with zero pending work
  and no rejections.
- Disposed_prepared_output_does_not_leak_into_replacement (capacities 1 and 3):
  retained candidate data is abandoned; repeated disposal and activation rejection
  are verified. The source finishes without completing/faulting the shared output.
  A fresh source then delivers only its own exact message and drains successfully.

The first test would detect simply linking later: the canonical source's dispatch
barrier would finish after discarding the data and postactivation captures would be
empty. Simply attaching early violates the zero-active-attachment assertion. This
closes an evidence gap in the previous test, which used a buffering source that
could retain messages even without a link.

These tests exercise full configured staging capacity; they do not independently
prove the exact rejection boundary at capacity + 1. They also do not test all-source
interleaving or every concurrent activation/disposal race. Do not infer those claims.

## Existing independent contracts inspected

In tests/Unit/Engine/Runtime/FluxFlow.Engine.Tests:

- ApplicationInputDispatchTests.Input_capacity_counts_active_work_and_retains_order:
  active plus queued work fills the configured bound, the next send reports Full,
  drain waits, and order/target completion are preserved.
- ApplicationInputDispatchTests.Rejected_work_precedes_queued_work_after_replacement:
  rejection and exception paths retain the original item ahead of queued items;
  stale attachment disposal cannot remove the replacement.
- ApplicationPortRuntimeTests.Input_revision_swap_finishes_claimed_work_and_moves_queued_work_to_new_target:
  a held native admission delays swap; the claimed item stays old and queued work
  goes new.
- ApplicationOutputCaptureTests.Capture_serializes_messages_and_propagates_bounded_backpressure:
  held capture prevents observations and eventually blocks producer admission;
  released captures and observations retain sequence.
- ApplicationOutputCaptureTests.Drain_waits_for_in_flight_capture_and_dispatches_before_completing:
  drain and receive remain incomplete until capture releases.
- ApplicationPortRuntimeTests.Prepared_revision_stages_outputs_and_swaps_routing_as_one_snapshot
  and Faulted_prepared_source_rejects_activation_without_changing_current_routing:
  route activation and failed-candidate isolation retain the current contract.

Current implementation anchors: src/Core/FluxFlow.Core/Nodes/FlowOutput.cs,
FlowDeliveryBatch.cs; src/Core/FluxFlow.Core/FlowRouter.cs; and the three boundary
classes under src/Engine/Runtime/FluxFlow.Engine/Ports.

## Consequence for the development plan

The remaining work should not repeat the broad question of whether these queues
exist. They do, and each serves a specific optional contract. Retain them for this
checkpoint; focus subsequent convergence checks on genuinely duplicated policy or
unverified equivalence, not class names. Fresh performance comparison remains
required, including the preceding bookkeeping edit. Broader feature/telemetry/provider
qualification and a fresh closed local package feed remain open. No production
implementation or package was changed by this audit. Nothing published.
