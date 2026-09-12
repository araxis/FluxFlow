# FluxFlow.Components.Storage.Composition

Optional runtime registration and authoring for storage put/get/query/delete.

Canonical FlowMessage ports carry requests, exact content and operation results.
Request materialization stays local; errors share Output and Events is diagnostic.
Store references, collections and query options remain flat. Borrowed stores and
clocks stay host-owned; factory-owned stores retain activation-scoped cleanup.

Editor hints are separate: install `FluxFlow.Components.Storage.Designer` and call
`AddStorageDesign()` after `AddStorage()`. Runtime registration alone does not
register a presentation catalog, activate components or resolve host resources.

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

`StorageComponents` exposes `StoragePut`, `StorageGet`, `StorageQuery`, and `StorageDelete` typed contracts. The retained `AddX` methods use those same contracts, and every handle exposes `Events`. See [typed code-first authoring](../../../../docs/39-typed-code-first-authoring.md).

A definition built from these contracts retains its executable descriptors.
Definition-based update hosting therefore calls only `AddFluxFlowUpdates(definition)` and
does not repeat the family registration above. Use that service registration
for JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contracts.
