# FluxFlow.Components.Assertions

Standalone expression assertions over canonical immutable values.

`AssertionNode<T>` accepts and emits `FlowMessage`. It materializes the owned
`FlowValue` into T locally and emits a structured `AssertionResult<T>` containing
that materialized input, pass flag, description/message, expression identity,
engine name, and evaluation timestamp. `JsonAssertionNode` is the schema-less
JSON specialization of the same implementation, without conversion queues.

Construct the component directly and add the instance to a workflow. All ports
use the same canonical message contract; T describes evaluation, not transport.

Pass and fail are valid assertion outcomes. Missing configuration or expression
evaluation failure becomes `FlowError` on the same Output. Incoming errors are
forwarded as the same message without evaluation. The package emits diagnostic Events and does not
require Engine or Composition.

The host provides `IFlowExpressionEngine`; an optional mapping context factory
may add immutable variables. Expressions compile during node construction.

Version 8.0.0-alpha.1 is an unpublished development preview, not a release candidate.

## Composition

Install `FluxFlow.Components.Assertions.Composition` for optional runtime registration and definition. This runtime package remains free of Composition, Designer, and Engine dependencies.
