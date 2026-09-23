# Processing lifetime owns terminal-input shutdown

FlowNode previously composed a second completion task around FlowProcessingLifetime
and delegated terminal controls to FlowControlInputs. Cancellation, joining and
fault coordination were split across all three classes.

FlowProcessingLifetime now accepts already-created native terminal blocks. It
keeps them open while accepted output drains, closes them afterwards, and waits for
all accepted actions before resolving Completion. Fault cancels their work and
faults the blocks along with other owned inputs/outputs. Lifetime does not construct
ports or handle message tracing. FlowNode does that when creating its ActionBlock.

Removed FlowNode's separate completion field, CompleteControlsAsync method, helper
instance and control-specific disposal path. It now delegates Completion and
DisposeAsync directly to the lifetime. Public signatures remain unchanged.

Added a regression for a terminal action failing after output completion while a
sibling action awaits cancellation. The initiating exception remains authoritative,
the sibling exits, and full Completion terminates. Existing staged drain, tracing,
backpressure, fault and cancellation tests remain applicable.

This is the processing-node step. FlowControlInputs is still used by FlowSource,
whose StopProduction/ProductionCompletion phase must keep controls alive until
explicit final shutdown. Moving that source lifecycle is the next bounded step;
this change does not claim the helper has been deleted or all lifetimes unified.
Evidence: artifacts/terminal-lifetime-20260918/.

Validation: Release build has zero warnings/errors. Full-suite run passed 2,441
tests; the source-declaration baseline check alone failed because two members of
the internal lifetime were initially declared public. Marked those new members
internal, rebuilt, and reran that check successfully. All 2,442 test cases are now
verified; the baseline file and consumer-facing signatures are unchanged.
