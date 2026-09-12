# FluxFlow.Components.FileSystem.Composition

Optional runtime registration and authoring for file read/write, directory enumeration, and file watch.

Ports carry canonical FlowMessage values. Components materialize their local
requests and emit file content, receipts, directory entries or changes. Errors
share Output; Events remains diagnostic. Path policy, recursion, limits and
runtime options stay flat. Clocks are borrowed from host-owned keyed resources.

Editor hints are separate: install `FluxFlow.Components.FileSystem.Designer` and call
`AddFileSystemDesign()` after `AddFileSystem()`. Runtime registration alone does not
register a presentation catalog, activate components or resolve host resources.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit FileSystemComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddFileSystem();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`FileSystemComponents` exposes `FileRead`, `FileWrite`, `DirectoryEnumerate`, and `FileWatch` typed contracts. The retained `AddX` methods use those same contracts, and every handle exposes `Events`. See [typed code-first authoring](../../../../docs/39-typed-code-first-authoring.md).

A definition built from these contracts retains its executable descriptors.
Definition-based update hosting therefore calls only `AddFluxFlowUpdates(definition)` and
does not repeat the family registration above. Use that service registration
for JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contracts.
