# FluxFlow.Components.Expectations

Standalone expectation evaluation over projection events.

`EventExpectationNode` accepts `ProjectionEvent` and emits
`EventExpectationResult`. Rule pass/fail, fail-fast behavior, and inclusion of
input/rule results are typed outcomes. Evaluation failure becomes `FlowError`
on the same Output.

The optional evaluator delegate and clock are host-owned. The package emits
Events, does not expose an Errors port, and does not require Engine or
Composition.

## Composition

Install `FluxFlow.Components.Expectations.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
