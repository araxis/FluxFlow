# FluxFlow.Components.Assertions.Composition

Optional registration and Designer metadata for `data.assert`.

The registration uses `JsonAssertionNode`: one `JsonElement` Input, one
`AssertionResult<JsonElement>` Output, and Events. Passed/failed results and
in-band errors are routed with link conditions; there are no Passed, Failed, or
Errors ports.

The expression engine is a required host-owned keyed resource. Context factory
and clock resources are optional. Option metadata groups expression, diagnostic,
type, result, branch, and runtime hints without owning those resources.

## Registration And Design Metadata

Register components with `RegisterAssertion`. `AssertionsComponentDesignMetadataProvider` supplies renderer-independent option, port, and host-owned resource hints for the Designer catalog.
