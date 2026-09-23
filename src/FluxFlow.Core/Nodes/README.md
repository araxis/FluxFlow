# Node contracts in Core

The `FluxFlow.Core.Nodes` namespace is part of `FluxFlow.Core`, not a separate
package in this development train. Components use it without the optional host,
portable definition or designer.

The current bases are `FlowNode`, `FlowTransformNode` and `FlowSource`.
FlowNode is now a 30-line explicit-emission specialization of FlowTransformNode;
both use the same native TransformMany processor, ports, diagnostics and lifetime.
It retains ProcessAsync and OnInputCompletedAsync for immediate streaming emissions.
FlowTransformNode uses TransformAsync and FlushAsync for returned result sequences.
`FlowValueTransformNode` is removed: payload-only processing uses the protected
`TransformAsync(message, transform)` helper on `FlowTransformNode`. It retains
message lineage, while the surrounding native transform execution retains ordered
concurrency, error forwarding and diagnostics. This is an intermediate reduction;
further consolidation of the remaining bases must preserve their behavior.
For an executable multi-output component, see `NumberRouter` in
`eng/core-consumer/Program.cs`. It implements construction and business processing;
application registration is supplied separately by the caller. The package consumer verifies both addressed outputs,
typed-payload identity, error recovery and owned-output shutdown on net8/net10.
Use FlowTransformNode for zero, one or many results with native ordered parallelism,
and FlowSource for started production. FlowNode supports existing explicit-emission
callers through the shared execution. Both support terminal controls. FlowTransformNode owns
side outputs through AddOutput<T>(); await EmitAsync(output, value, token) to send
to them. Their direct emissions are not reordered by EnsureOrdered: use serial
processing when side effects require input order. Already-sent side outputs cannot
be rolled back if later processing fails.
Override FlushAsync to return final main-output messages after processing
drains. It may also await side-output sends. Flush is skipped on fault; flush failures
fault lifecycle Completion because there is no single input message to derive an
error from. Final main-output sequences are validated before any are published.
Explicit-emission FlowNode forwards errors directly inside processing, keeping
backpressure and tracing active until admission. Returned-result components forward
results through the native output drain. Direct emissions are never transactional.
FlowTransformNode uses TransformManyBlock. Override Task<IEnumerable<FlowMessage>>
TransformAsync(message): return [] to drop, [message] to forward unchanged, or
derived messages created with message.WithValue(value). Return a finite sequence;
the framework materializes it inside the error/tracing boundary before publishing.
An enumeration failure produces one error instead of partial results. A very large
result sequence therefore requires corresponding memory; bounded input/output
capacity does not impose a per-operation result count limit.
Their message ports carry non-generic `FlowMessage`, whose payload is an owned
`FlowValue` or `FlowError`. The former generic bases have been removed, with
no aliases or adapter runtime. Client serialization helpers remain at boundaries.

Construct a component with its actual dependencies. Its native blocks are exposed
as properties; neither IFlowNode nor the bases declare application registration.
When using a workflow, the caller supplies names in the existing Add callback.
IFlowSource only adds startup and staged shutdown. TerminalInputs identifies
terminal blocks for lifecycle ordering, not naming.

## Data and identity

`JsonFlowValue` snapshots supplied JSON; typed `FlowValue` subclasses own immutable state and create JSON lazily. Ordinary
forwarding and fanout share the exact message and payload. Components deserialize
only their local processing models. A derived `WithValue(...)` message preserves
trace/correlation/headers and records new message/causation identity.

`FlowError` holds domain failure data. Byte ownership and transport content
metadata belong to component payloads, not a separate Core content contract. A record alone is not deep immutability; referenced
state must be owned at ingress.

## Processing and lifetime

One native bounded processing block owns node intake/execution. Reliable bounded
outputs apply downstream backpressure. Events are best-effort and do not block
workflow data. Extra outputs belong to the same component lifetime.

Native filtered links can exclude individual values. Matching offers support
postponement, reservation and consumption through the original source. A thin
per-offer callback adapter delegates that state to the native sender; it does
not retain a payload queue or introduce another scheduler. Permanently declining
targets still fault reliable delivery. Reserved offers remain available for
native settlement even when their link is removed.

Incoming errors forward unchanged by default. Processing exceptions become error
data; infrastructure/lifecycle failures fault `Completion`. Sources start once,
honor cancellation and stop producing before accepted work drains.

Await normal emissions and completion. Processing nodes accept native
`ExecutionDataflowBlockOptions` (capacity, parallelism, ordering, scheduler and
cancellation). Without options, input capacity is 128; explicitly supplied options
keep Dataflow defaults, including unbounded input unless BoundedCapacity is set.
All three bases accept `outputCapacity` directly (default 128, strictly positive).
FlowNode.AddInput(process) and FlowTransformNode.AddInput(process) create an additional ordinary native input with the
node's execution options and shared error handling. Declare inputs in the
constructor. Application callers name them through their Add callback. They are owned by
the node: Complete closes admission, accepted work drains before output completion,
and Fault/cancellation stops all inputs. Terminal control inputs remain a separate
shutdown concept; Gate Open/Close are ordinary inputs, not terminal controls.
AddControlInput's name is only a nonblank diagnostic label. It does not register
a port, need address syntax, or have to be unique. Terminal inputs are owned by
block identity; application composition assigns and validates actual addresses.
For FlowNode, FlowProcessingLifetime owns ordinary and terminal block shutdown:
ordinary input drains first, outputs drain next, terminal inputs close last.
Completion includes every terminal action. Block construction and tracing stay in
FlowNode. FlowSourceLifetime owns source production and control shutdown; FlowSource
constructs blocks and supplies its producer delegate. StopProduction ends production
and drains outputs while controls remain available. Complete also closes controls;
full Completion waits for accepted actions. The old FlowControlInputs helper is removed.
For example: `base(new ExecutionDataflowBlockOptions { BoundedCapacity = 32,
MaxDegreeOfParallelism = 4 }, outputCapacity: 32)`. Use the protected Stopping token
for cancelable component work. Graph builders do not silently replace instance
settings. Reliability is in-process, not persistence or replay.

Optional host packages are absent from the minimum-framework branch.

Current value design: FlowValue.Json is lazy; component-owned immutable subclasses can bypass JSON for typed processing. JsonFlowValue holds dynamic JSON. FlowError is also a FlowValue; canonical FlowMessage.Value exposes its single payload even on failure. See docs/20-flow-data-contracts.md.
