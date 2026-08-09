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

## Code-first authoring

`StorageComponents` exposes `StoragePut`, `StorageGet`, `StorageQuery`, and `StorageDelete` typed contracts. The retained `AddX` methods use those same contracts, and every handle exposes `Events`. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from these contracts retains its executable descriptors.
Normal code-first hosting therefore calls only `AddFluxFlow(definition)` and
does not repeat the family registration above. Use that service registration
for JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contracts.
