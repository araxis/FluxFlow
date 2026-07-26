# FluxFlow.Components.State.Composition

Optional registration and Designer metadata for keyed JSON state reduction.

The configured node uses `JsonStateReducerNode`, with
`StateReducerInput<JsonElement>` Input,
`StateReducerResult<JsonElement>` Output, and Events. Errors share Output and
are routed with normal conditions; there is no Errors port.

Reducer/key expressions, initial state, key limits, diagnostic caps, and
runtime hints remain flat. Expression engine is required; clock and context
resources are optional and host-owned.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and exactly one StateComponentDesignMetadataProvider metadata provider through `IServiceCollection`:

```csharp
services.AddStateComponents();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.
