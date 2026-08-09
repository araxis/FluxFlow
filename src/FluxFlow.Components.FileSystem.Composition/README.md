# FluxFlow.Components.FileSystem.Composition

Optional registration and Designer metadata for file read/write,
directory enumeration, and file watch.

Metadata declares the typed runtime contracts: `FileReadRequest`,
`FileReadContent`, `FileContentWriteRequest`, `FileWriteResult`,
`DirectoryEntry`, and `FileChange`. Errors share normal Output and Events
remains diagnostic.

Base path, patterns, recursion, limits, timing, and runtime options stay flat.
The optional clock is a host-owned keyed resource. Composition does not own
file-system handles beyond the activated node lifecycle.

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

`FileSystemComponents` exposes `FileRead`, `FileWrite`, `DirectoryEnumerate`, and `FileWatch` typed contracts. The retained `AddX` methods use those same contracts, and every handle exposes `Events`. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from these contracts retains its executable descriptors.
Normal code-first hosting therefore calls only `AddFluxFlow(definition)` and
does not repeat the family registration above. Use that service registration
for JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contracts.
