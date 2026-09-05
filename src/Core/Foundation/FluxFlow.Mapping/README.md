# FluxFlow.Mapping

The engine-free expression/mapping abstraction for FluxFlow — a small leaf package
that nodes and the engine both build on.

## When To Use It

Use this package when a node, adapter, or host needs expression-driven mapping or
predicate contracts without depending on the engine. It defines the small
interfaces and context objects that let callers plug in their own expression
language or plain delegates.

Do not put concrete expression-language setup, composition resource lookup, or
workspace schema decisions in this package. Hosts and adapter packages own those
choices; mapping-aware nodes consume these contracts through constructor
dependencies or composition resources.

## Public Surface

- `IFlowExpressionEngine` / `IFlowCompiledExpression` — host-provided expression
  compilation/evaluation (DynamicExpresso, JSONata, plain C#, …).
- `IFlowMapper` / `IFlowPredicate` — map a value / decide a condition; with
  `ExpressionFlowMapper` / `ExpressionFlowPredicate` (expression-driven) and
  `DelegateFlowMapper` / `DelegateFlowPredicate` (delegate-driven) adapters.
- `FlowMapContext` / `IFlowMapContextFactory` — the variable context an expression
  evaluates against.

`FlowMapContext` snapshots assigned variables with ordinal key comparison. This
keeps a per-message mapping context stable even if the caller later mutates the
dictionary used to create it.

Expression mapper and predicate adapters compile their expressions during
construction and fail fast if a host expression engine returns a null compiled
expression. Delegate adapters validate their delegates at construction.

These are pure abstractions (no dependencies). They let a node do conditional/mapping
work against a host-supplied expression engine without referencing the runtime engine —
the configuration layer that reads C# / JSONata strings and compiles them lives above.
