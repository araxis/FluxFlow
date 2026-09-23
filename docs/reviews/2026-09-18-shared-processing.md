# One processing implementation, two authoring contracts

FlowNode now derives from FlowTransformNode and adapts ProcessAsync to an empty
returned sequence. Its existing final flush hook adapts to the shared lifetime.
The duplicated processor setup, ports, emission helpers, diagnostics/error boundary
and lifecycle delegation were removed. Both use native TransformManyBlock.
Combined base code is 343 -> 233 lines; FlowNode itself is 30 lines.

The explicit-emission contract remains because some components await output
admission during execution. Deferring these sends until return would change
backpressure and tracing duration. A private-protected mode preserves direct error
forwarding/recovery and requested-cancellation behavior for that contract. This is
not a public policy setting or additional authoring requirement. Initial tests
caught changed error ordering and span duration; those behaviors were restored
without weakening tests.

Terminal-input construction is now in the shared base; lifetime still owns their
shutdown. No RegisterPorts responsibility was added. FlowNode's ProcessAsync and
OnInputCompletedAsync remain. The newly introduced finite-result hook on
FlowTransformNode is renamed FlushAsync to keep the two contracts unambiguous.
Existing finite-result overrides were migrated. Native Input is now a
TransformManyBlock for FlowNode too; the load qualification harness's concrete cast
was updated after its tests exposed that assumption. Consumers should rebuild for
this preview inheritance/protected-hook/native-implementation change.

This removes duplicate implementation, not the two class names. Source and
processing lifetimes remain separate. No new base class or tracing redesign.
Evidence: artifacts/shared-processing-20260918/.

Validation: Release build is clean. The full run passed all suites except 25 Engine
load cases with the obsolete ActionBlock cast. After updating that harness, all 536
Engine cases passed on rerun. All 2,446 distinct cases are verified. Fresh Core and
Routing package consumers passed net8/net10; source API baseline is updated.
