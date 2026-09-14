# FluxFlow.Components.Storage.Composition

Optional runtime registration and definition for storage put/get/query/delete.

Canonical FlowMessage ports carry requests, exact content and operation results.
Request materialization stays local; errors share Output and Events is diagnostic.
Store references, collections and query options remain flat. Borrowed stores and
clocks stay host-owned; factory-owned stores retain activation-scoped cleanup.

## DI Registration

This adapter registers direct node factories through `IServiceCollection`:

```csharp
services.AddStorage();
```

The resulting type-to-factory dictionary is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first definition

`AddStoragePut`, `AddStorageGet`, `AddStorageQuery`, `AddStorageDelete` retain typed options and named port handles. They embed the
same callbacks used by DI in ApplicationSetup.ComponentFactories. Build the setup
with application services or pass it to AddFluxFlowUpdates. Repeating the family
DI registration is optional: matching delegates are idempotent and conflicts fail.
Links validate the actual ports after node construction. Factories remain in process
and cannot be exported as JSON; the flat JSON configuration shape is unchanged.

The former StorageComponents contract container is removed. Standalone node
construction remains available. Shared host resources are not disposed by components.

Owned leases transfer disposal through context.SetLifetime; borrowed leases remain shared. Failed constructors release acquired leases before returning an error.
