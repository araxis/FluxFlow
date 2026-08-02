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
