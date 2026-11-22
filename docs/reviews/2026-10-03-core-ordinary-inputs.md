# Shared native ordinary-input processing

## Findings

Most bundled processing components use FlowTransformNode to return zero or more
messages. Batch uses FlowNode for timer-driven publication and final flushing.
Those distinct authoring styles remain useful; neither wrapper is removed.

FlowTransformNode previously constructed its main TransformManyBlock directly, but
AddInput constructed an ActionBlock with a nested adapter and manual loop to publish
returned errors. Both already shared the same tracing/error boundary. The extra
command adapter was allocated while processing each message.

## Change

One private CreateProcessor method now constructs and links the native TransformManyBlock
for both the main input and ordinary additional inputs. AddInput adapts its action once
during construction: successful completion returns no results. Forwarded and generated
errors follow the native link. No custom block, new class or public contract was added.

Native EnsureOrdered now governs returned errors within each additional transform
input, including concurrent commands. Previously the manual command publication loop
could deliver errors in completion order even with EnsureOrdered enabled. There is
no new ordering guarantee between different input ports. Direct EmitAsync calls,
including FlowNode error forwarding, retain their explicit-emission semantics.

The existing lifetime waits for every ordinary input to complete, which now includes
draining its transform results, before flushing and closing outputs. Terminal controls
remain separate ActionBlocks: they must remain available for downstream feedback until
business processing ends. No source, Application or Configuration code changed.

## Verification

Full Release suite: 1,995 passing executions, zero failures or warnings, using the
existing .NET 8/.NET 10 target matrix. Isolated Core and Routing package consumers
pass on both targets. Full-suite evidence is in the temporary flux-core-unified-inputs
directory. The public source-declaration baseline is unchanged and passes.

Added tests cover ordered/unordered concurrent command failures, ordinary-input
draining before flush, forwarding the original error envelope without invoking the
command, and no output from successful commands. Existing tests cover cancellation,
fault propagation, active-command shutdown and Gate Open/Close behavior.

Public API signatures remain unchanged. No commit, push or publication.
