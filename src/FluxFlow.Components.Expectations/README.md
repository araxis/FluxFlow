# FluxFlow.Components.Expectations

A standalone event-expectation node for FluxFlow.

## What it is

`EventExpectationNode` is a self-contained TPL Dataflow processor built on the
`FluxFlow.Nodes` kit. You give it options, post `ProjectionEvent`s to its input,
and it resolves **exactly once** into a single `EventExpectationResult` broadcast
on its output (failures on the error port, diagnostics on the event port). It
needs **nothing else** — no engine, registry, or runtime:

```csharp
await using var node = new EventExpectationNode(new EventExpectationOptions
{
    Kind = EventExpectationNodeKind.Expect,
    Name = "order-completed",
    TimeoutMilliseconds = 5000,
    Filter = new EventFilter
    {
        Type = "operation.completed",
        Status = "ok",
        SubjectPrefix = "orders/"
    }
});

node.Output.LinkTo(asserter.Input);  // broadcast: link to as many consumers as you like

await node.Input.SendAsync(FlowMessage.Create(@event));
```

An `Expect` node is satisfied when a matching event arrives; a `Guard` node is
satisfied when none arrives. The node resolves on the first of three triggers:

- a matching event,
- a configured timeout (armed over the injected `TimeProvider`),
- input completion via `CompleteWithResultAsync()`.

`EventExpectationOptions` validates at construction: non-positive
`TimeoutMilliseconds`, negative `MaxObservedEvents`, negative `MaxPreviewChars`,
or non-positive `BoundedCapacity` fails fast as an argument exception.

## Ports

| Port | Block | Purpose |
|------|-------|---------|
| `Input` | `BufferBlock<FlowMessage<ProjectionEvent>>` | bounded intake — `SendAsync` applies backpressure |
| `Output` | `BroadcastBlock<FlowMessage<EventExpectationResult>>` | the single result, fanned out to every linked consumer |
| `Errors` | `BroadcastBlock<FlowError>` | evaluation failures |
| `Events` | `BroadcastBlock<FlowEvent>` | `event.expectation.matched` / `.timed-out` / `.completed` diagnostics |

The result carries the correlation id of the matching event (or, on timeout and
completion, the last observed event's id) so correlation flows event → result via
the `FlowMessage<T>` envelope.

## Result

`EventExpectationResult` includes:

- evaluated timestamp
- expectation name
- expectation kind
- satisfied flag
- matched flag
- timeout flag
- matched event summary when one exists
- recent observed event summaries
- filter copy
- reason text

## Filters

Expectations use the same neutral `EventFilter` contract as the Projections
package (the shared `ProjectionEvent` / `EventFilter` / `EventFilterMatcher`
contracts come from `FluxFlow.Components.Projections`). Filters support:

- event type or type prefix
- subject prefix
- channel prefix
- excluded subject prefix
- excluded channel prefix
- status
- source
- source node id
- component id through an event attribute named `componentId`
- attribute key/value pairs
- event timestamp range

Filters use ordinal string comparison.

## Timing

Pass a `TimeProvider` to the constructor for deterministic timeout and result
timestamps in tests or hosts. The timeout is armed via `TimeProvider.CreateTimer`,
so a `FakeTimeProvider` drives it by advancing the clock — no real-time wait.

```csharp
var node = new EventExpectationNode(options, timeProvider);
```

## Composition

Building a workflow — reading config, creating nodes, linking them — is a
separate concern from the node. This package is just the standalone node.

Use `FluxFlow.Components.Expectations.Composition` when a
`FluxFlow.Composition` host should register the optional `event.expectation`
factory:

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterEventExpectation());
```

The composition adapter binds `EventExpectationOptions` from node configuration
and can resolve an optional keyed `TimeProvider` resource named `clock`.

The optional composition package also exposes
`ExpectationsComponentDesignMetadataProvider` for neutral Designer metadata over
the `event.expectation` composition node type. The standalone Expectations
package remains free of Designer, Composition, and Engine dependencies.

`CompleteWithResultAsync()` remains a direct node lifecycle feature in v1.
Composition runtime stop uses normal node completion. Use the direct node API
when a completion-result flush is required until composition grows an explicit
final-flush lifecycle hook.
