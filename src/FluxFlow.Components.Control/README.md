# FluxFlow.Components.Control

Compatibility expression-driven control nodes for FluxFlow. `FilterNode<T>`
and `WhenNode<T>` are obsolete because the canonical workflow model evaluates
conditions directly on links.

No runtime behavior was removed. Existing code-authored workflows can continue
using the nodes while definitions migrate.

## Canonical Replacement

A filter is one conditioned link. A true/false branch is two links with
complementary conditions:

```json
{
  "Resources": {},
  "Workflows": {
    "Orders": {
      "Normalize": {
        "Type": "data.map",
        "Output": [
          {
            "Port": "Priority.Input",
            "Condition": "payload.priority == 'High'"
          },
          {
            "Port": "Standard.Input",
            "Condition": "payload.priority != 'High'"
          }
        ]
      },
      "Priority": {
        "Type": "orders.priority"
      },
      "Standard": {
        "Type": "orders.standard"
      }
    }
  }
}
```

Composition compiles each distinct condition once per activation. At runtime a
condition failure rejects only that link, reports runtime diagnostics, and does
not stop sibling links or the host. Output fan-out and shared target inputs are
already part of canonical link behavior, so a separate router adds no domain
result.

## Compatibility Nodes

| Node | Shape | Behavior |
|------|-------|----------|
| `FilterNode<TInput>` | `Input` -> `Output` | Emits matching messages and drops nonmatches. |
| `WhenNode<TInput>` | `Input` -> `WhenTrue` / `WhenFalse` | Routes each message to one branch; `Output` aliases `WhenTrue`. |

Both nodes remain standalone and usable without Engine or Composition. They
accept either a compiled `IFlowPredicate<TInput>` or an
`IFlowExpressionEngine` plus optional `IFlowMapContextFactory<TInput>`, compile
expressions once, preserve message correlation, expose Events, and report
evaluation failures through their released Errors ports.

```csharp
#pragma warning disable CS0618
await using var node = new FilterNode<OrderMessage>(
    options,
    expressionEngine,
    contextFactory,
    clock);
#pragma warning restore CS0618
```

`InputType` remains diagnostic metadata and `BoundedCapacity` controls the
standalone node queue. Invalid options continue to fail construction before an
input pipeline is created.

## Composition Compatibility

`FluxFlow.Components.Control.Composition` keeps the released closed-generic
factories and Designer metadata for legacy definitions. New canonical
definitions should express filtering and branching on their output links and
should not register `flow.filter` or `flow.when`.
