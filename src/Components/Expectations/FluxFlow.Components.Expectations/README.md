# FluxFlow.Components.Expectations

Standalone expectation evaluation over projection events.

Processing keeps the original `FlowMessage` beside the parsed `ProjectionEvent`.
Match, timeout and completion results use canonical messages directly, without
intermediate typed envelopes. The node retains one last observed envelope,
including its JSON payload, for timeout/completion lineage. The existing lock,
timer queue and one-time resolution behavior are preserved.

Event deserialization lives locally in `EventExpectationNode`. Composition uses
`ProjectionInputShapes.Event` from Projections.Contracts. The former public
`ProjectionEventMaterializer` has been removed; metadata callers should use that
shape property, and direct parsing callers should deserialize locally or send a
`FlowMessage` to the node. Malformed input still resolves the expectation once with
`projection.event.invalid_shape` and an `event.expect.rejected` diagnostic.

`EventExpectationNode` accepts `ProjectionEvent` and emits
`EventExpectationResult`. Rule pass/fail, fail-fast behavior, and inclusion of
input/rule results are typed outcomes. Evaluation failure becomes `FlowError`
on the same Output.

The optional evaluator delegate and clock are host-owned. The package emits
Events, does not expose an Errors port, and does not require Engine or
Composition.

## Composition

Install `FluxFlow.Components.Expectations.Composition` for optional runtime registration and definition. This runtime package remains free of Composition, Designer, and Engine dependencies.
