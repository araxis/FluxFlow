# FluxFlow.Components.Expectations

Standalone projection-event expectations for FluxFlow.

## Canonical Node

`EventExpectationNode` consumes `FlowMessage<ProjectionEvent>` and resolves
exactly once through `FlowMessage<FlowResult<EventExpectationResult>>` on
`Output`. It also exposes lifecycle and result diagnostics through `Events`.
It does not require Engine or Composition.

```csharp
await using var node = new EventExpectationNode(
    new EventExpectationOptions
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

node.Output.LinkTo(resultConsumer.Input);
await node.Input.SendAsync(FlowMessage.Create(@event));
```

The canonical node emits these `FlowResult.Kind` values:

| Kind | Meaning | `IsError` |
|------|---------|-----------|
| `Matched` | An expected event matched. | `false` |
| `Unmet` | A guarded event matched and violated the guard. | `false` |
| `TimedOut` | The configured timeout resolved the expectation. | `false` |
| `Completed` | Ordered input completion resolved the expectation. | `false` |
| `EvaluationFailed` | Expected filter evaluation failed. | `true` |

`EventExpectationResult.Satisfied` carries the rule decision. Timeout and
completion satisfy a Guard and leave an Expect unmet. These outcomes are normal
workflow data; they do not use a universal error port. Static option errors
still reject construction or activation, and unexpected block faults remain on
`Completion`.

`Complete()` drains accepted input and then emits the completion variant if no
earlier match, timeout, or evaluation failure won. `CompleteWithResultAsync()`
does the same and waits for node completion. Every trigger races through one
exact-once claim.

Output messages derived from an observed event preserve its correlation,
trace, and headers, create a new message identity, and record the input message
as causation. Timeout or completion without any observed event starts a new
exchange.

## Options

| Option | Default | Meaning |
|--------|---------|---------|
| `Kind` | `Expect` | Expect a matching event or guard against one. |
| `Name` | `null` | Optional result name. |
| `Filter` | match all | Projection-event filter. |
| `TimeoutMilliseconds` | `null` | Optional positive timeout. |
| `MaxObservedEvents` | `10` | Recent event summaries retained in the result. |
| `MaxPreviewChars` | `256` | Maximum payload-preview characters retained per summary. |
| `BoundedCapacity` | `128` | Maximum queued inputs. |

The optional `TimeProvider` constructor argument controls timeout and result
timestamps deterministically. The node snapshots the filter at construction.

## Typed Result Boundary

`FlowResult<EventExpectationResult>` is a real payload type. Links do not
implicitly unwrap `Value`; route or extract it explicitly when a downstream
component expects `EventExpectationResult`.

## CLR Boundaries

`EventExpectationNode` is the single maintained Expectations runtime. Hosts
route results through `FlowResult.Kind`, `IsError`, and `Error.Code`, and read
the optional `Value` for matched, unmet, timeout, and completion details. The
package does not expose a direct-result compatibility node, numeric error
codes, or a universal Errors stream.

## Composition

This runtime package owns standalone nodes, options, filters, result contracts,
and diagnostics. It does not own JSON definitions, host resources, routing,
rendering, or engine lifecycle. Optional registration and Designer metadata are
provided by `FluxFlow.Components.Expectations.Composition`.
