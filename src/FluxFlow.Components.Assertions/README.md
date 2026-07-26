# FluxFlow.Components.Assertions

Standalone typed expression assertions.

`AssertionNode<T>` accepts T and emits `AssertionResult<T>` containing the exact
input, pass flag, description/message, expression identity, engine name, and
evaluation timestamp. `JsonAssertionNode` is the explicit schema-less JSON
specialization.

Pass and fail are valid assertion outcomes. Missing configuration or expression
evaluation failure becomes `FlowError` on the same Output. Incoming errors are
propagated without evaluation. The package emits diagnostic Events and does not
require Engine or Composition.

The host provides `IFlowExpressionEngine`; an optional mapping context factory
may add immutable variables. Expressions compile during node construction.

## Composition

Install `FluxFlow.Components.Assertions.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
