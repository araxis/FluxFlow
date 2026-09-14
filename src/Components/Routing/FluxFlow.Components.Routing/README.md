# FluxFlow.Components.Routing

Standalone stateful routing components. Version 8.0.0-alpha.1 is an unpublished development preview; migration is in progress, not release-ready.

| Node | Input | Output value |
|------|-------|--------------|
| `WindowNode<T>` | canonical message; T materialized inside the component | canonical value containing `FlowWindow<T>` |
| `CorrelationNode<T>` | canonical message; T materialized inside the component | canonical value containing a match or timeout outcome |
| `JoinNode<TLeft,TRight>` | separate canonical Left/Right inputs; local typed materialization | canonical value containing a match or timeout outcome |

JSON specializations are available for schema-less configuration workflows.
All routing components use the same canonical Core transport and output dispatcher;
their JSON specializations do not add conversion blocks. Construct either directly
and add the instance to a workflow. Send `FlowMessage.Create(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))`
and materialize a returned window with `message.Value.Json.Deserialize<FlowWindow<T>>()`.
Existing input errors pass through with the original envelope. This intentionally
breaks the former typed routing-port APIs. Correlation and join results retain their
existing `match`/`timeout` JSON shapes and can be materialized as the corresponding
concrete outcome. Join exposes only its actual Left/Right inputs, with bounded native
admission and ordered shared-state processing. It has no redundant input buffers or
command-kind switch. Component completion waits for active work before disposal.
The optional Composition adapter is separate; publication remains pending.

Selectors are typed delegates. Window completion, correlation match/timeout,
and join match/timeout are typed outcomes. Selector/capacity/processing failure
becomes `FlowError` on the same Output.

Nodes preserve FIFO and timeout behavior, bounded pending state, exact-once
completion races, lineage, fan-out, and Events. Structural Switch/Fork/Merge
behavior belongs to canonical links rather than runtime routing nodes.

## Composition

Install `FluxFlow.Components.Routing.Composition` for optional component construction and registration. This runtime package remains free of Composition, Designer, and Engine dependencies.


## Gate

`GateNode` starts closed by default. `Input` receives ordinary canonical messages;
`Open` and `Close` are terminal signal inputs whose payloads are ignored. When Input
is processed, an open gate forwards the original message and a closed gate drops its
value. No buffering for later reopening, serialization or new message envelope is
introduced. Existing error envelopes still pass through, including while closed.

```csharp
var gate = new GateNode(initiallyOpen: false, boundedCapacity: 128);
var component = application.AddWorkflow("main").Add("gate", gate);
// Link producer outputs to component.Input, component.Inputs["Open"] or
// component.Inputs["Close"], and component.Output to the next component.
```

The three input blocks execute independently. Sending a control accepts it into its
queue; it does not synchronously acknowledge the state change. `IsOpen` exposes the
current state. Concurrent Open/Close/data arrivals have no global ordering promise.
A message already admitted to Output is not retracted by Close. Normal bounded
output backpressure still applies. Completion drains accepted output and controls;
explicit faults cancel controls and release blocked input. Shared Core processing
owns tracing, metrics, error forwarding and control lifetime; Gate contains no
component-specific telemetry code.
