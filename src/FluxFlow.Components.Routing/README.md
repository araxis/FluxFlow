# FluxFlow.Components.Routing

Standalone routing nodes for FluxFlow. The canonical nodes consume immutable
`FlowValue` messages and emit `FlowResult<T>` on one normal output, so matches,
timeouts, windows, and expected routing failures can all participate in normal
workflow links. No engine, registry, or composition host is required.

## Canonical Nodes

| Node | Input | Output |
|------|-------|--------|
| `FlowValueWindowNode` | `FlowValue` | `FlowResult<FlowWindow<FlowValue>>` |
| `FlowValueCorrelationNode` | `FlowValue` | `FlowResult<FlowCorrelationOutcome<FlowValue>>` |
| `FlowValueJoinNode` | `Left: FlowValue`, `Right: FlowValue` | `FlowResult<FlowJoinOutcome<FlowValue,FlowValue>>` |

Every node also exposes `Events` for diagnostics. Expected operation failures
are error-shaped results on `Output`; there is no universal Errors port.

```csharp
var node = new FlowValueCorrelationNode(
    new CorrelationRoutingOptions
    {
        RequestSide = "request",
        ResponseSide = "response",
        TimeoutMilliseconds = 30_000
    },
    keySelector: value => value.GetObject()["key"].GetString(),
    sideSelector: value => value.GetObject()["side"].GetString());

node.Output.LinkTo(results);
await node.Input.SendAsync(FlowMessage.Create(requestValue));
```

Selectors are ordinary host-provided delegates. Compile them once through an
expression package when definitions use expressions; Routing does not choose
an expression language or resolve CLR types from strings.

## Result Contract

Window results use these success kinds:

- `window.count`: the item boundary emitted the window.
- `window.time`: the time boundary emitted the window.
- `window.completed`: input completion emitted the final partial window.

Correlation and Join use `matched` and `timed-out` success kinds. Their values
are explicit discriminated records:

- `FlowCorrelationMatchedOutcome<TInput>` or
  `FlowCorrelationTimedOutOutcome<TInput>`
- `FlowJoinMatchedOutcome<TLeft,TRight>` or
  `FlowJoinTimedOutOutcome<TLeft,TRight>`

Selector, key, side, capacity, and other expected operation failures use
`operation-failed`, set `IsError`, and carry the stable
`routing.operation_failed` error code. The original routing error code is
retained in error details. Later messages continue processing.

All emitted messages preserve source lineage with `FlowMessage<T>.With(...)`.
A correlation match keeps the request correlation id, a join match keeps the
left correlation id, and each timeout or expected failure keeps the originating
message correlation id.

## Windowing

`WindowRoutingOptions` requires at least one boundary. `MaxItems` emits when a
window fills; `TimeMilliseconds` emits when the open window ages out. When both
are set, whichever fires first wins. A partial window is emitted on completion
by default. Timer expiry and completion are serialized so a claimed window is
emitted exactly once.

## Correlation And Join

Correlation pairs request and response values on one input by a key selector
and side selector. Join pairs values from separate `Left` and `Right` inputs by
their key selectors. Both enforce `TimeoutMilliseconds` and `MaxPending`, use
FIFO matching for repeated Join keys, and emit remaining pending values as
normal timeout outcomes.

All timing uses an injected `TimeProvider`, defaulting to
`TimeProvider.System`, so timeout behavior is deterministic under a fake clock.

## Structural Routing Migration

Version 5 removes `FlowSwitchNode<TInput>`, `FlowForkNode<TInput>`, and
`FlowMergeNode<TInput>`. Canonical workflow links provide conditional routing,
one-to-many fan-out, and many-to-one input fan-in without dedicated structural
nodes.

Use complementary conditions for true/false branches. Use one condition per
route plus a condition that excludes every named route for a default branch.
Condition failures are reported through runtime diagnostics and system events;
they do not stop healthy sibling links or the host.

Links preserve payloads and never create route envelopes. Add an explicit
mapper before the links when downstream components need route metadata in the
payload. Migrate all structural node usages before upgrading.

## Typed Compatibility

The released generic nodes remain available for strongly typed code-authored
workflows:

```csharp
var node = new FlowJoinNode<RequestMessage, ResponseMessage>(
    new JoinRoutingOptions { TimeoutMilliseconds = 30_000 },
    leftKeySelector: request => request.CorrelationId,
    rightKeySelector: response => response.CorrelationId);
```

`FlowWindowNode<TInput>`, `FlowCorrelationNode<TInput>`, and
`FlowJoinNode<TLeft,TRight>` preserve their direct match, timeout, Errors, and
Events surfaces. There is no implicit conversion between these typed contracts
and canonical `FlowValue` or `FlowResult<T>` links.

## Lifecycle

Canonical and typed nodes implement `IFlowNode`. `Complete()` drains
accepted input before completing outputs, `Fault(exception)` faults data
outputs, and `DisposeAsync()` completes, drains, and releases timers. Component
faults remain local to the node and do not define host lifetime.

## Composition

The optional `FluxFlow.Components.Routing.Composition` package registers the
canonical `flow.window`, `flow.correlate`, and `flow.join` factories through
parameterless registration methods. Explicit generic overloads remain for
typed compatibility under host-selected node type names.
