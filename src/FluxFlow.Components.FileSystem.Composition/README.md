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

## Registration And Design Metadata

Register components with `RegisterDirectoryEnumerate`, `RegisterFileRead`, `RegisterFileWatch`, `RegisterFileWrite`. `FileSystemComponentDesignMetadataProvider` supplies renderer-independent option, port, and host-owned resource hints for the Designer catalog.
