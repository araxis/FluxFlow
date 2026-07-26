# FluxFlow.Components.Mapping.Composition

Optional configuration registration and Designer metadata for `data.map`.

`AddMappingComponents()` registers the configured `data.map` component with one `JsonElement` Input, one
`JsonElement` Output, and Events. It is intentionally JSON-oriented because
configuration-driven documents are schema-less; code-authored workflows use
`FlowMapperNode<TInput,TOutput>` directly for known CLR contracts.

The host supplies a keyed `IFlowExpressionEngine`; context factory and clock
resources are optional. Mapping errors travel on Output and can be routed by
`isError` and `error.code`. There is no Failed or Errors port.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and exactly one MappingComponentDesignMetadataProvider metadata provider through `IServiceCollection`:

```csharp
services.AddMappingComponents();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.
