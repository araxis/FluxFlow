# FluxFlow.Components.FileSystem.Composition

Optional runtime registration and definition for file read/write, directory enumeration, and file watch.

Ports carry canonical FlowMessage values. Components materialize their local
requests and emit file content, receipts, directory entries or changes. Errors
share Output; Events remains diagnostic. Path policy, recursion, limits and
runtime options stay flat. Clocks are borrowed from host-owned keyed resources.

## DI Registration

This adapter registers direct node factories through `IServiceCollection`:

```csharp
services.AddFileSystem();
```

The resulting type-to-factory dictionary is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first definition

`AddFileRead`, `AddFileWrite`, `AddDirectoryEnumerate`, `AddFileWatch` retain typed options and named port handles. They embed the
same callbacks used by DI in ApplicationSetup.ComponentFactories. Build the setup
with application services or pass it to AddFluxFlowUpdates. Repeating the family
DI registration is optional: matching delegates are idempotent and conflicts fail.
Links validate the actual ports after node construction. Factories remain in process
and cannot be exported as JSON; the flat JSON configuration shape is unchanged.

The former FileSystemComponents contract container is removed. Standalone node
construction remains available. Shared host resources are not disposed by components.
