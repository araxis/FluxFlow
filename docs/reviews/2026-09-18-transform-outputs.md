# TransformMany side outputs and flush proof

The remaining base overlap includes real multi-output and final-flush behavior.
FlowTransformNode now offers owned side outputs and a finite-result flush hook,
using the existing FlowProcessingLifetime. No extra base or execution abstraction.

NumberRouter in the standalone package consumer now derives from FlowTransformNode.
Positive messages return [message]; negative messages await a send to Negative and
return []. The same consumer verifies addressed output delivery, typed payload
identity, recovery and completion against a freshly packed Core package.

OnInputCompletedAsync returns final main-output messages after processing and
ordinary inputs drain. It can await side-output sends. Lifetime skips flush on
fault and completes outputs after successful flush. Invalid or failing enumeration
faults lifecycle without partially publishing the final main-output sequence.
Existing flush/owned-output/disposal cases now exercise TransformMany; an additional
test verifies lazy flush failure and side-output fault propagation.

Main returned results retain Dataflow ordering. Side emissions and external effects
occur during execution and are not reordered by EnsureOrdered. Serial execution is
needed when those effects require input order. Already emitted side messages are
not rolled back if later processing fails. Flush errors remain lifecycle faults,
since no single input envelope identifies the retained batch.

Validation: 2,446 passing tests in nine full reports; Release build has zero warnings
or errors. Fresh Core package consumer passes net8/net10. Source API baseline was
reviewed/refreshed; existing public signatures are retained, with protected authoring
capabilities added. Evidence: artifacts/transform-outputs-20260918/.

FlowNode remains. This step establishes that multi-output and flush can use the
return-based model, but does not claim reduced total base code yet. Remaining
explicit-emission and terminal-control scenarios need review before removing or
consolidating it. FlowTracing is unchanged.
