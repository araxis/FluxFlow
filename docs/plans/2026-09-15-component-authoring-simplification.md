# Component classes over native Dataflow blocks

## Current continuation — 2026-09-18

The historical steps below describe earlier checkpoints, not today's complete
remaining-work list. Registration is now outside IFlowNode and every component
base. FlowMessage creation/derivation is Create/WithValue; FlowValue and the
envelope live together in Messaging. Headers carry immutable-by-contract FlowValue
instances. Namespace alignment and the lifetime-construction cleanup are complete.

The user explicitly deferred further FlowTracing size/location work. Leave its
implementation and package placement alone in this Core simplification pass.
The standalone JSONata comparison is also complete and outside this solution.

Next review, in order:

1. **Component inputs and lifecycle boundary.** FlowControlInputs currently calls
   ApplicationAddress.RequireConstructedSegment for its local control label, while
   composition separately assigns port addresses. Determine which label behavior is
   genuinely needed for diagnostics and which address constraints are misplaced.
   Preserve terminal controls until accepted output/feedback drains; ordinary Gate
   Open/Close inputs are not terminal controls. Do not simply delete the coordinator
   or its name argument and lose diagnostics/shutdown behavior.
2. **Wrapper implementation.** Review repeated completion/error/output ownership in
   FlowNode, FlowTransformNode and FlowSource. Retain three wrappers unless a simpler
   implementation demonstrably preserves zero/many results, ordered parallel
   transforms and staged source shutdown. One mode-switching base is not the goal.
3. **Request/reply surface.** Review FlowRequestReply and its use of the generic
   exchange coordinator against actual callers. Preserve capacity, cancellation,
   timeout during admission, duplicate/late reply handling and independent concurrent
   correlations; do not replace it with an unbounded dictionary for line reduction.
4. **Closeout.** Reconcile current docs/API with the resulting implementation,
   remove proven obsolete code only, and run full regressions plus relevant package
   consumers for any public contract changes. Record retained complexity and the
   deferred tracing decision. A review finding is not an implemented simplification.

Latest verified implementation baseline: 2,428 passing tests, nine reports, Release
build without warnings/errors (artifacts/propagator-fields-20260918). No new runtime
change was made as part of this continuation review.

## Historical checkpoints

Agreed direction: keep one class per component. Do not make developers choose a
growing hierarchy of FluxFlow-specific execution models. A component owns native
ITargetBlock<FlowMessage>/ISourceBlock<FlowMessage> ports, registered under unique
addresses. Small helper methods and a simple optional base should handle common
ownership/error work. Preserve reliable delivery, ordered concurrent processing,
error-as-data, diagnostics and staged source shutdown while simplifying usage.

FlowMessage<T> is wanted, with `where T : FlowValue`. Preserve the actual CLR
payload and its lazy JSON representation. Ports continue carrying the shared
FlowMessage envelope; typed helpers must not become another transport. Consolidate
identity, error and serialization behavior rather than maintaining two independent
message implementations. Do not remove generic messages as previously proposed.

Small steps:

1. Remove FlowContent and its converter from minimum Core; content-specific byte
   handling belongs to consuming components. Completed; see the removal report.
2. Migrate generic message and runtime port APIs together to FlowValue constraints,
   with typed-payload identity/JSON/error regression tests and package qualification.
   Completed in the typed-message step. Generic message, runtime port and DI
   message APIs now constrain payloads to FlowValue. See the typed-message report.
3. Demonstrate a component class using one simple base plus native blocks/helpers;
   preserve multi-port support. Migrate and remove redundant node bases only after
   ordered transforms, error forwarding and terminal-control shutdown are proven.
   Pending. Do not silently replace ordered TransformBlock behavior with an
   unordered ActionBlock, or expose tracing/logging code in every component.

   Review on 2026-09-15: processing ownership is already shared internally;
   source shutdown must retain its production/control phases. Required output
   emission now shares one internal FlowOutput method across all three bases.
   This removes duplicated delivery error handling without adding a new public
   base. The one-base proof remains pending; see the shared-emission review.

   On 2026-09-16, IFlowComponent was removed: IFlowNode now owns optional instance
   registration and terminal-input identity. Add callbacks remain supported.
   Authors no longer choose between overlapping node/component contracts. The
   three bases still remain; see the node-contract review for migration and tests.

   The subsequent package-only NumberRouter proof demonstrates a multi-output
   component with only construction, registration and processing code. It passes
   on net8/net10, including typed identity, error recovery and output shutdown.
   Review recommendation: retain the three existing wrappers for now; replacing
   them with mode flags does not simplify authoring. The common-base replacement
   remains unproven, not completed. See the component-authoring-proof review.

No direct-native-block Add overload was requested as a replacement for component
classes. DI changes, optional modules, publication and external project changes
are not part of this step.
# TransformMany checkpoint — 2026-09-18

The user selected native zero/one/many processing as the next simplification.
Implemented in the existing FlowTransformNode, with Gate as the production proof.
TransformAsync now returns Task<IEnumerable<FlowMessage>>; finite results are
materialized inside the processing boundary, so lazy enumeration failure becomes
error data and does not fault Dataflow. Ordinary command inputs share error/lifetime
handling. No new base class or component registration mechanism was added.

Verified: 2,436 passing tests, Release build without warnings/errors, and fresh
Core/Routing package consumers on net8/net10. See
../reviews/2026-09-18-transform-many.md. Next: review actual FlowNode multi-output,
flush and terminal-control callers before consolidation. FlowControlInputs remains;
its removal is not demonstrated by this step. Keep FlowTracing redesign deferred.
