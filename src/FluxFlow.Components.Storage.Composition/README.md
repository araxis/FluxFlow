# FluxFlow.Components.Storage.Composition

Optional registrations and Designer metadata for storage put/get/delete/query.

Descriptors use the runtime package's typed requests/outcomes, one Output, and
Events. Store references, names, query options, and runtime hints remain flat.
Stores and clocks are host-owned keyed resources. Errors share Output; there is
no Sessions or Errors compatibility branch.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit StorageComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddStorage();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.
