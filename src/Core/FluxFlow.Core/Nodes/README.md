# Node contracts in Core

The `FluxFlow.Nodes` namespace is part of `FluxFlow.Core`, not a separate
package in this development train. Components use it without the optional host,
portable authoring or designer.

`FlowNode` and `FlowSource` are the only provided node/source implementations.
Their message ports carry non-generic `FlowMessage`, whose payload is an owned
`FlowValue` or `FlowError`. The former generic bases have been removed, with
no aliases or adapter runtime. Client serialization helpers remain at boundaries.

Construct a component with its actual dependencies; register its instance with
a named workflow when graph addressing/lifecycle is needed. Standard bases
expose their actual port inventory through `IFlowComponent`. Custom components
can implement that small contract around native blocks.

## Data and identity

`FlowValue` snapshots mutable ingress data and owns its JSON storage. Ordinary
forwarding and fanout share the exact message and payload. Components deserialize
only their local processing models. A derived `With(...)` message preserves
trace/correlation/headers and records new message/causation identity.

`FlowContent` owns exact immutable bytes plus content metadata. `FlowError`
holds domain failure data. A record alone is not deep immutability; referenced
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

Await normal emissions and completion. Configure capacities with
`FlowNodeOptions`/`FlowSourceOptions`; graph builders do not silently replace
instance settings. Reliability is in-process, not persistence or replay.

The optional host still has port/delivery consolidation work outstanding. Removal
of generic node bases does not claim that all runtime duplication is resolved.
