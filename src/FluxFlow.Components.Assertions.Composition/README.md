# FluxFlow.Components.Assertions.Composition

Optional registration and Designer metadata for `data.assert`.

The registration uses `JsonAssertionNode`: one `JsonElement` Input, one
`AssertionResult<JsonElement>` Output, and Events. Passed/failed results and
in-band errors are routed with link conditions; there are no Passed, Failed, or
Errors ports.

The expression engine is a required host-owned keyed resource. Context factory
and clock resources are optional. Option metadata groups expression, diagnostic,
type, result, branch, and runtime hints without owning those resources.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and exactly one AssertionsComponentDesignMetadataProvider metadata provider through `IServiceCollection`:

```csharp
services.AddAssertionsComponents();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.
