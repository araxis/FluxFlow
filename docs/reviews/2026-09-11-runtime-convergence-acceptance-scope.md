# Runtime convergence: acceptance scope check

This note prevents an implementation ledger from silently creating architectural
requirements beyond the approved plan. It does not declare runtime convergence done.

## Authoritative requirements

The approved implementation prompt, phase 2, explicitly requires ordinary startup/
drain to be separate from revisions. Phase 5 requires updates and dynamic links to
reattach to Core, preserving stable addresses, in-flight work, rollback and disposal,
without creating another execution engine. Structural acceptance requires exactly
one canonical transport runtime, fanout policy implementation and pending exchange
implementation, with no forbidden Core dependencies or per-message authoring work.

The corresponding plan, section 9, prohibits parallel typed/canonical transport
runtimes, a hidden second fanout kernel and duplicate pending-exchange implementations.
It does not require an otherwise unused FlowApplication instance in every revision,
nor identical orchestration classes for ordinary and revision-specific lifetime policy.

Source documents: docs/plans/2026-09-08-core-refactoring-implementation-prompt.md,
sections 5 and 6; docs/plans/2026-09-08-core-refactoring-plan.md, section 9.

## Current source observations

- Core FlowExecution builds ordinary routers, starts components through FlowLifetime,
  handles producer-driven completion and includes router completion in application
  completion. FlowApplication owns the direct topology and request admission.
- ApplicationRuntimePreparation still builds component/resource snapshots, stable port
  generations and a revision candidate. ApplicationRuntimeRevisionCandidate starts
  components through the same Core FlowLifetime after port activation, and retains
  revision leases, output-fault observation and resource cleanup.
- ApplicationRuntimeDrainPlan uses the shared dependency ordering but owns staged
  stable-input/output barriers and source/control drain policy. Those revision-specific
  barriers cannot simply be replaced by ordinary producer completion.
- The migrated ApplicationOutputPort uses the Core FlowOutput queue and delivery
  executor. Capture, revision route selection and post-admission observations remain
  optional policy outside Core.

These observations identify separate orchestration policies. They do not, by class
names alone, prove or disprove a second transport executor. Nor does sharing a helper
prove that every actual route and lifetime edge is correct.

## Required next audit

Trace direct, fluent, JSON and revision-enabled message paths from actual component
construction through admission, routing, processing, fanout, reply and completion.
For each stage, identify the concrete queue/block, implementation owner, capacity,
snapshot boundary, cancellation owner and disposal owner. Locate and remove any real
duplicate transport/fanout/exchange executor. Prove policy-only differences with
behavioral tests for held capture, generation changes, fan-in, fault and cleanup.

Do not close the convergence gate merely because current helper names match. Equally,
do not introduce a god runtime, another queue, a creator hierarchy or a nominal unused
Core wrapper simply to make the execution paths look textually identical. An adapter
must remove a real duplicate or centralize an actual invariant to earn its place.

The existing ledger's phrase "whole-graph/lifetime convergence" remains an open audit,
not a new requirement that every host must instantiate the ordinary FlowApplication
class. Acceptance must be decided against the approved semantic and structural gates,
with current source and executable evidence. No gate is marked passed by this note.
