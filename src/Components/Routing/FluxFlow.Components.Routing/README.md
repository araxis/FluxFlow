# FluxFlow.Components.Routing

Standalone typed stateful routing nodes.

| Node | Input | Output value |
|------|-------|--------------|
| `WindowNode<T>` | T | `FlowWindow<T>` |
| `CorrelationNode<T>` | T | `FlowCorrelationOutcome<T>` |
| `JoinNode<TLeft,TRight>` | separate Left/Right inputs | `FlowJoinOutcome<TLeft,TRight>` |

JSON specializations are available for schema-less configuration workflows.
Selectors are typed delegates. Window completion, correlation match/timeout,
and join match/timeout are typed outcomes. Selector/capacity/processing failure
becomes `FlowError` on the same Output.

Nodes preserve FIFO and timeout behavior, bounded pending state, exact-once
completion races, lineage, fan-out, and Events. Structural Switch/Fork/Merge
behavior belongs to canonical links rather than runtime routing nodes.

## Composition

Install `FluxFlow.Components.Routing.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
