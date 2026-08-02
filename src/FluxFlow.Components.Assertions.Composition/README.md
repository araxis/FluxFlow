# FluxFlow.Components.Assertions.Composition

Optional registration and Designer metadata for `data.assert`.

`AssertionsComponentDefinition` is the package-owned source of canonical type,
option, resource, port, and Designer presentation declarations.

The registration uses `JsonAssertionNode`: one `JsonElement` Input, one
`AssertionResult<JsonElement>` Output, and Events. Passed/failed results and
in-band errors are routed with link conditions; there are no Passed, Failed, or
Errors ports.

The expression engine is a required host-owned keyed resource. Context factory
and clock resources are optional. Option metadata groups expression, diagnostic,
type, result, branch, and runtime hints without owning those resources.

## DI Registration

This optional application-integration adapter uses one designed component
registration. Its `ComponentDescriptor` owns the canonical type, options,
resources, processing capabilities, and typed ports; Designer metadata adds
presentation hints without redefining that structure:

```csharp
services.AddFluxFlowComponents().AddAssertions();
```

The resulting `ComponentCatalog` and `ComponentDesignMetadataCatalog` are built
once from DI registrations. Standalone runtime nodes remain usable without this
package, and referenced external resources remain host-owned.
