# Update runtime convergence audit

This checkpoint inspects the existing execution paths; it does not authorize a
new runtime or declare the entire refactoring complete.

## Current migration: canonical output executor

ApplicationOutputPort no longer owns an ActionBlock or a fanout batch loop. It uses
Core FlowOutput's existing bounded native ingress and delivery implementation. The
host prepares a selected route snapshot after capture under its revision gate, then
releases the gate before Core starts/joins target admissions. Existing revision routes
and native fanout links implement the same internal admission contract. Host observations
run only after successful delivery. Core contains no revision/capture/observation types.

The old output DispatchAsync loop is removed. Barrier draining and pending counts now
come from the Core queue; hosted drain additionally respects the revision gate. Native
public output routing retains its direct dispatch entry and full source protocol.
Qualification and exact tests are recorded in .testagent/dispatch-convergence-review.md.

This removes the duplicate output executor, not every remaining convergence gap.
The host still constructs routing snapshots and orchestrates candidate/stable-input
lifetimes separately from FlowApplication/FlowExecution. Whole-graph construction and
lifetime convergence must be demonstrated, not inferred from this executor migration.

## Shared behavior and deliberate update policy

### Follow-up: executable condition changes

A further source trace found an identity gap in route-only revisions: source/target
and condition text could stay identical while bound parameters or host-native
configuration changed. Such a replacement previously skipped affected output gates,
so a held capture could finish under the newly swapped condition. Whole-application
replacements can mask this by independently gating their attached outputs.

The existing routing identity now retains the conditional compiled-link reference.
New compilation is conservatively a change; reuse of that link and equivalent
unconditional routes remain unchanged. Duplicate declarations intentionally ignore
the executable identity. No predicate-equivalence engine, semantic hash, new queue,
public API, or Core dependency was introduced. Existing capture/selection gates and
the snapshot transaction perform the actual coordination.

Ten new cases verify changed endpoint sets, same-instance/unconditional behavior,
held capture with parameter/native-configuration replacement, cancellation recovery
and duplicate declarations. Five cases reproduced the defect before the fix. Exact
evidence and current qualification: .testagent/revision-condition-review.md. This
preserves an optional update contract; it does not by itself close the full graph,
telemetry, performance and fresh-package acceptance gates.

| Concern | Ordinary execution | Optional update execution |
| --- | --- | --- |
| Component processing | Native component blocks and Core processing lifetime | The same activated component instances and native blocks |
| Reliable fanout admission | FlowOutput uses FlowDeliveryBatch | The same FlowOutput executor, after host capture and route preparation |
| Request/reply | FlowApplication uses FlowRequestReply | ApplicationPortRuntime uses FlowRequestReply; typed APIs project at the boundary |
| Startup and first failure | FlowExecution uses FlowLifetime | ApplicationRuntimeRevisionCandidate uses FlowLifetime |
| Dependency ordering | Frozen graph layers and entry components use FlowDependencyOrder | Drain preparation uses FlowDependencyOrder with existing ordinal key order and host-specific edge exclusions |
| Topology/lifetime orchestration | Frozen topology; no generations or rollback | Candidate preparation, stable address generations, attachment swaps and staged drains |

Source anchors: src/Core/FluxFlow.Core/FlowExecution.cs, FlowRouter.cs,
FlowLifetime.cs, FlowRequestReply.cs, Nodes/FlowOutput.cs and
Nodes/FlowDeliveryBatch.cs; src/Engine/Runtime/FluxFlow.Engine/Hosting/
ApplicationRuntimePreparation.cs, ApplicationRuntimeRevisionCandidate.cs,
ApplicationRuntimeDrainPlan.cs, ApplicationRuntimePortGeneration.cs; Ports/
ApplicationInputPortCore.cs, ApplicationOutputPort.cs and ApplicationPortRuntime.cs.

Ordinary contract activation already constructs Core FlowApplication in
src/Core/Application/FluxFlow.Composition/Authoring/ContractApplicationExtensions.cs.
Core has no project reference to authoring or operations. Internal friend access
lets the optional packages reuse the small kernel without making it public API.

The update ingress block holds an item while an attachment is paused or replaced.
Its native target send remains awaitable. The output block serializes capture,
freezes route selection during a revision, retains the in-flight route snapshot,
and implements drain barriers. These are explicit update/capture boundaries, not
component processing implementations. Replacing them blindly with ordinary
FlowApplication would discard stable-address and in-flight contracts. They must
remain outside Core even as shared routing semantics are brought into agreement.

## Fixed in this checkpoint

### Capture and routing boundary audit

The pre-migration source trace identified a concrete constraint on convergence:
FlowRouter configures a fixed route array on FlowOutput, whose admitted deliveries
retain their link snapshot. ApplicationOutputPort instead awaits capture while holding
the revision dispatch gate, selects the current revision routes, releases that gate,
then awaits native target admission. ApplicationOutputLink explicitly cancels a
manually unlinked admission; revision route snapshots remain valid across activation.
FlowValuePreparedApplicationOutput also holds a bounded staging block before activation.
These behaviors are exercised by capture, held-dispatch, unlink and revision tests.
Concrete existing cases include Capture_completes_before_revision_links_connected_links_receives_and_observations,
Drain_waits_for_in_flight_capture_and_dispatches_before_completing,
Input_revision_swap_finishes_claimed_work_and_moves_queued_work_to_new_target and
Prepared_revision_stages_outputs_and_swaps_routing_as_one_snapshot.

Consequently, constructing an otherwise unused FlowApplication in revision preparation
would not converge delivery. A real replacement must retain capture-before-selection,
dispatch-time revision selection, in-flight route ownership and stable-input generation
semantics while removing the host's independent business-route dispatch. Core must not
gain revision IDs, capture stores or operational coordination. This remains unresolved;
the payload/topology qualification checkpoint extends actual-path evidence before that
replacement, rather than asserting that shared helpers already satisfy the requirement.

Source anchors: FlowRouter.cs and Nodes/FlowOutput.cs in Core; Ports/
ApplicationOutputPort.cs (the former DispatchAsync, CaptureAndStartDispatchAsync and DeliverAndNotifyAsync;
now PrepareDispatchAsync, CaptureAsync, PrepareDelivery and NotifyDelivered),
ApplicationOutputConnections.cs and ApplicationOutputAttachments.cs in Engine. The link
compiler already rejects data cycles before activation; the optional drain fallback is
not evidence that configured cyclic workflows are supported.

The later shared dependency-planning checkpoint removes the independent topological
sort from ApplicationRuntimeDrainPlan and the second root search in FlowExecution.
Core retains the frozen ordering used for validation. Optional drain still owns its
stage barriers and source/control completion policy; self/outside/control exclusions
remain explicit at that boundary. This is algorithm reuse, not a claim that the host
already executes the same frozen Core graph. See dependency-order-review.md and its
qualification artifacts for exact evidence and remaining limits.

Staged drain invoked source StopProduction and final Complete callbacks directly.
A callback exception could escape without the candidate's FlowLifetime faulting
the remaining components. Cancelled component/port waits also escaped that failure
policy. DrainAsync now sends every escaping failure to its existing FlowLifetime
and rethrows; the coordinator retains the first failure and attempts every Fault
callback even if one throws. Graceful stage order and resource ownership do not
change. No queue, interface, creator, public API or dependency was added.

Six new cases in Hosting/RevisionComponentLifetimeTests.cs cover stop/final
completion errors, staged/fallback cancellation, successful staged production
drain, sibling failure propagation and exactly-once disposal. Four cases failed
against the original production code; the fallback stop and graceful cases passed.
Cancellation checks preserve cancellation/token identity and shared sibling
failure identity, without requiring reference equality across cancelled task
representations. Callback failure reference identity is asserted directly.

## Explicit output policies: implemented, qualification in progress

Both routing paths now complete selection through FlowRouteSelection. An unmatched
message fails unless a reply role, fallback, explicit discard or isolated condition
failure accounts for it. Captures, receives and observations do not satisfy reliable
business routing. Sinks without outputs remain valid; exposed emitting terminal
outputs explicitly opt into discard. Named event outputs used only for observation
also declare terminal intent. System event/diagnostic outputs keep the host's
explicit best-effort policy.

Portable component lists DiscardUnmatched and RepliesFrom are read by one shared
validator. Fluent output.DiscardUnmatched() and application.RepliesFrom(output)
produce those same lists. Compiled address sets flow through direct construction
and immutable revision snapshots; policy-only changes participate in source drain
gates and become effective on activation. Explicit replies suppress fallback for
late/duplicate traffic, while matching ordinary routes remain eligible.

Dependent component, durability and health fixtures now declare exact terminal
outputs; the single-component helper remains strict by default. A new negative
hosted test reproduced a real failure: a faulted output dispatcher could strand
its producing component. Each active candidate now awaits the existing output
completion signals and forwards failure to its shared FlowLifetime. Disposal
cancels and joins these waits before releasing ports, so retired revisions cannot
observe later failures of shared outputs. No new dispatch queue or event broker
was introduced. Real bounded source/node tests cover both drain paths and an
unfailed sibling output; a separate test checks revision observation ownership.

Direct typed document binding now excludes the two graph-policy arrays from
component options, while strict unknown-option validation remains enabled.
Held-dispatch tests check reply policy on both sides of a revision activation.
The source-declaration baseline review is limited to four added Composition
declarations and internal Engine policy/completion declarations; Core's public
surface is unchanged. Focused suites pass. Final whole-solution qualification is
recorded in .testagent/output-policy-followup-review.md when complete; earlier
failed and timed-out migration runs remain historical evidence, not passing runs.

After that semantic convergence, the outstanding measured gates remain the wider
condition/telemetry and sustained-load matrix, fresh isolated local package
candidate and final feature/package handoff. Existing regression passes are not
substitutes for those gates. No publication or external consumer coordination.
