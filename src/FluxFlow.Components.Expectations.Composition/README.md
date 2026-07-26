# FluxFlow.Components.Expectations.Composition

Optional registration and Designer metadata for `event.expect`.

Metadata declares `ProjectionEvent` Input, `EventExpectationResult` Output,
and Events. Rules, evaluation, result, diagnostic, and runtime options remain
flat. Optional evaluator and clock resources are host-owned and carry delegate
and clock picker hints. Errors share Output.

## Registration And Design Metadata

Register components with `RegisterEventExpectation`. `ExpectationsComponentDesignMetadataProvider` supplies renderer-independent option, port, and host-owned resource hints for the Designer catalog.
