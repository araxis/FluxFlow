# Source lifetime and removal of FlowControlInputs

FlowSource now defines native outputs and terminal ActionBlocks, invokes component
behavior and traces control execution. It delegates lifecycle to the internal
FlowSourceLifetime, which receives blocks and the production delegate rather than
constructing ports or processing messages. FlowControlInputs is deleted.

The source lifetime owns both shutdown phases. StopProduction cancels production
and drains data outputs; terminal controls remain available. Complete also closes
control admission; full Completion joins accepted terminal actions. Fault stops
both phases, retaining the initiating exception. Source production and controls
still have separate cancellation tokens because stopping production must not abort
acknowledgements of accepted downstream work.

FlowSource is 336 -> 126 lines. Source plus lifetime is 424 lines versus the former
source plus control helper's 404. This separates responsibilities and removes one
mixed-purpose helper; it is not a net line-count reduction or a new public authoring
concept. Processing and source lifetimes remain distinct because their shutdown
contracts differ. No new interfaces, registration system or tracing redesign.

Added regression: a control can fail after successful ProductionCompletion;
full Completion reports that failure and sibling controls stop. The existing suite
also verifies staged drain, bounded admission, cancellation, source failure, tracing,
owned outputs and exactly-once disposal.

Validation: final Release build has zero warnings/errors. Full suite passed 2,441
cases with only the source-declaration baseline needing refresh for deletion of
the internal helper and delegated source implementation. Reviewed and updated the
baseline; its normal check passed. After adding the regression, final Nodes runs
passed 341 cases on each of net8/net10. Across these runs all 2,444 cases are verified.
Fresh Core and Routing package consumers pass on net8/net10. Evidence is under
artifacts/source-lifetime-20260918/. No public signature or package dependency change.

Next review the remaining FlowNode/FlowTransformNode overlap and the actual
multi-output/flush use cases before attempting a smaller shared authoring contract.
