# FluxFlow.Mapping

The engine-free expression/mapping abstraction for FluxFlow — a small leaf package
that nodes and the engine both build on.

- `IFlowExpressionEngine` / `IFlowCompiledExpression` — host-provided expression
  compilation/evaluation (DynamicExpresso, JSONata, plain C#, …).
- `IFlowMapper` / `IFlowPredicate` — map a value / decide a condition; with
  `ExpressionFlowMapper` / `ExpressionFlowPredicate` (expression-driven) and
  `DelegateFlowMapper` / `DelegateFlowPredicate` (delegate-driven) adapters.
- `FlowMapContext` / `IFlowMapContextFactory` — the variable context an expression
  evaluates against.

These are pure abstractions (no dependencies). They let a node do conditional/mapping
work against a host-supplied expression engine without referencing the runtime engine —
the configuration layer that reads C# / JSONata strings and compiles them lives above.
