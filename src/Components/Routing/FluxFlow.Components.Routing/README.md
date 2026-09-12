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
and add the instance to a workflow. Send `FlowMessage.Create(FlowValue.From(value))`
and materialize a returned window with `message.Value.Deserialize<FlowWindow<T>>()`.
Existing input errors pass through with the original envelope. This intentionally
breaks the former typed routing-port APIs. Correlation and join results retain their
existing `match`/`timeout` JSON shapes and can be materialized as the corresponding
concrete outcome. Join exposes only its actual Left/Right inputs, with bounded native
admission and ordered shared-state processing. It has no redundant input buffers or
command-kind switch. Component completion waits for active work before disposal.
Optional authoring/editor separation and final release verification remain pending.

Selectors are typed delegates. Window completion, correlation match/timeout,
and join match/timeout are typed outcomes. Selector/capacity/processing failure
becomes `FlowError` on the same Output.

Nodes preserve FIFO and timeout behavior, bounded pending state, exact-once
completion races, lineage, fan-out, and Events. Structural Switch/Fork/Merge
behavior belongs to canonical links rather than runtime routing nodes.

## Composition

Install `FluxFlow.Components.Routing.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
