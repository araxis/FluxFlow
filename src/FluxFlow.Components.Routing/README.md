# FluxFlow.Components.Routing

Standalone routing nodes for FluxFlow, built on the **FluxFlow.Nodes** kit. Every node is
a self-contained TPL Dataflow processor: `new` it with its options and the selectors it
needs, post `FlowMessage<T>` envelopes to its input(s), and link its broadcast output/error/
event ports to the next stage. No engine, registry, or runtime is required. Key and side
extraction is supplied by the caller as plain delegates (compile them once from a
`FluxFlow.Mapping` `IFlowExpressionEngine` / `IFlowPredicate` if you use expressions).

## Nodes

| Node | Base | Shape |
|------|------|-------|
| `FlowSwitchNode<TInput>` | `FlowNode<TInput, TInput>` | `Input` -> `Matched` (primary `Output`), `Default`, optional `Routed`, configured route-output ports, `Errors`/`Events` |
| `FlowForkNode<TInput>` | `FlowNode<TInput, TInput>` | `Input` -> each configured output (first is the primary `Output`), `Errors`/`Events` |
| `FlowMergeNode<TInput>` | `FlowNode<TInput, TInput>` | one fan-in `Input` (link many upstreams) -> `Output`, `Errors`/`Events` |
| `FlowWindowNode<TInput>` | `FlowNode<TInput, FlowWindow<TInput>>` | `Input` -> `Output` (windows), `Errors`/`Events` |
| `FlowCorrelationNode<TInput>` | `FlowNode<TInput, FlowCorrelationMatch<TInput>>` | `Input` -> `Matched` (primary `Output`), `Timeouts`, `Errors`/`Events` |
| `FlowJoinNode<TLeft, TRight>` | kit primitives (two inputs) | `Left`, `Right` -> `Output` (results), `Timeouts`, `Errors`/`Events` |

Every emitted message carries the source correlation id forward (`FlowMessage<T>.With`):
the matched branch keeps the input's id, a join result keeps the left message's id, a
correlation match keeps the request's id, and each timeout keeps its own message's id.

All nodes time off an injected `System.TimeProvider` (defaulting to `TimeProvider.System`),
so windows, join timeouts, and correlation timeouts are deterministic under a
`FakeTimeProvider` in tests.

## Switch

```csharp
var node = new FlowSwitchNode<AppMessage>(
    new SwitchRoutingOptions
    {
        Routes = ["priority", "standard"],
        RouteOutputs = new Dictionary<string, string> { ["priority"] = "Priority" },
        DefaultRoute = "unknown"
    },
    routeKeySelector: message => message.Category);
```

`Matched` (the primary `Output`) re-emits the input when its route key is in `Routes`;
`Default` re-emits it otherwise. If `Routes` is empty every non-empty key is treated as
matched. `RouteOutputs` adds extra ports keyed by name and re-emits the input to the
matching route port; several route keys may map to the same port. Set `EmitRouteEnvelope`
to expose a neutral `Routed` port. `EmitMatchedInput` / `EmitDefaultInput` suppress those
branches. Route-key selector failures surface on `Errors` and the node keeps processing.

## Fork

```csharp
var node = new FlowForkNode<AppMessage>(
    new ForkRoutingOptions { Outputs = ["Audit", "Transform", "Dashboard"] });
```

Each configured output receives every input. The first output is the primary `Output`;
the rest are reached through `node.Outputs[name]`. Output names must be valid identifiers
and cannot collide with the built-in `Input`/`Errors` ports.

## Merge

```csharp
var node = new FlowMergeNode<AppMessage>(new MergeRoutingOptions());
// link several upstreams into the one input:
sourceA.LinkTo(node.Input);
sourceB.LinkTo(node.Input);
```

A fan-in node: the single bounded `Input` already merges concurrent producers, and the
node re-broadcasts each message on `Output` in arrival order, preserving correlation.

## Window

```csharp
var node = new FlowWindowNode<AppMessage>(
    new WindowRoutingOptions { MaxItems = 100, TimeMilliseconds = 5000 });
```

`Output` emits `FlowWindow<TInput>` (sequence, items, start/emit timestamps, duration,
count, reason). `MaxItems` emits when the window fills; `TimeMilliseconds` emits when the
open window ages out (timed off the injected clock); when both are set, whichever fires
first wins. At least one boundary is required. On completion a partial window is emitted by
default — set `EmitPartialOnCompletion = false` to discard it.

## Correlation

```csharp
var node = new FlowCorrelationNode<AppMessage>(
    new CorrelationRoutingOptions
    {
        RequestSide = "request",
        ResponseSide = "response",
        TimeoutMilliseconds = 30000
    },
    keySelector: message => message.CorrelationId,
    sideSelector: message => message.Kind);
```

Pairs a request with its matching response by key. `Matched` emits
`FlowCorrelationMatch<TInput>`; `Timeouts` emits `FlowCorrelationTimeout<TInput>` for
pending inputs that age past the timeout (observed before the next input or on completion).
Invalid keys/sides, duplicate sides, selector failures, and pending-capacity overflow
surface on `Errors` and the node keeps processing.

## Join

```csharp
var node = new FlowJoinNode<RequestMessage, ResponseMessage>(
    new JoinRoutingOptions { TimeoutMilliseconds = 30000 },
    leftKeySelector: request => request.CorrelationId,
    rightKeySelector: response => response.CorrelationId);
```

The one two-input routing node, built directly on kit primitives. Post to `Left` and
`Right`; `Output` emits `FlowJoinResult<TLeft, TRight>` for matched pairs (FIFO for repeated
keys) and `Timeouts` emits `FlowJoinTimeout<TLeft, TRight>` for values that age past the
timeout or remain when the node completes. Key-evaluation failures, empty keys, and
pending-capacity overflow surface on `Errors` and the node keeps processing.

## Lifecycle

Each node implements `IFlowNode`: `Complete()` drains and completes the outputs, `Fault`
faults the data outputs while flushing (completing) `Errors`/`Events` so buffered
diagnostics survive, and `await DisposeAsync()` completes, drains, and releases timers.

## Composition

The optional `FluxFlow.Components.Routing.Composition` package registers
closed generic routing factories for `FluxFlow.Composition`. The adapter binds
the existing routing options from node configuration and resolves host-owned
keyed selector delegates plus optional keyed `TimeProvider` resources.

```csharp
services.AddKeyedSingleton<Func<AppMessage, string?>>(
    "route",
    message => message.Category);

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterSwitch<AppMessage>()
        .RegisterFork<AppMessage>()
        .RegisterMerge<AppMessage>()
        .RegisterWindow<AppMessage>()
        .RegisterCorrelation<AppMessage>()
        .RegisterJoin<RequestMessage, ResponseMessage>());
```

Use custom node type strings for multiple input shapes, for example
`flow.switch.order`, `flow.window.http`, and `flow.join.request-response`.
Selector expressions are not compiled by the composition adapter; compile or
create delegates in the host and expose them as keyed resources such as
`routeKeySelector`, `keySelector`, `sideSelector`, `leftKeySelector`, and
`rightKeySelector`.
