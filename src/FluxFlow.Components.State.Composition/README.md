# FluxFlow.Components.State.Composition

Optional registration and Designer metadata for keyed JSON state reduction.

The configured node uses `JsonStateReducerNode`, with
`StateReducerInput<JsonElement>` Input,
`StateReducerResult<JsonElement>` Output, and Events. Errors share Output and
are routed with normal conditions; there is no Errors port.

Reducer/key expressions, initial state, key limits, diagnostic caps, and
runtime hints remain flat. Expression engine is required; clock and context
resources are optional and host-owned.

## Registration And Design Metadata

Register components with `RegisterStateReducer`. `StateComponentDesignMetadataProvider` supplies renderer-independent option, port, and host-owned resource hints for the Designer catalog.
