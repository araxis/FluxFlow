# FluxFlow.Components.Projections.Composition

Optional registration and Designer metadata for `event.project`.

The descriptor exposes `ProjectionEvent` Input,
`EventProjectionSnapshot` Output, and Events. Filter, rate, emission, preview,
diagnostic, and runtime options remain flat. The optional clock is host-owned.
Errors share Output and are selected by normal link conditions.

## Registration And Design Metadata

Register components with `RegisterEventProjection`. `ProjectionsComponentDesignMetadataProvider` supplies renderer-independent option, port, and host-owned resource hints for the Designer catalog.
