# FluxFlow.Components.Mapping.Composition

Optional configuration registration and Designer metadata for `data.map`.

`RegisterMapper()` registers `JsonMapperNode` with one `JsonElement` Input, one
`JsonElement` Output, and Events. It is intentionally JSON-oriented because
configuration-driven documents are schema-less; code-authored workflows use
`FlowMapperNode<TInput,TOutput>` directly for known CLR contracts.

The host supplies a keyed `IFlowExpressionEngine`; context factory and clock
resources are optional. Mapping errors travel on Output and can be routed by
`isError` and `error.code`. There is no Failed or Errors port.

## Registration And Design Metadata

Register components with `RegisterMapper`. `MappingComponentDesignMetadataProvider` supplies renderer-independent option, port, and host-owned resource hints for the Designer catalog.
