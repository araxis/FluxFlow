# FluxFlow.Components.FileSystem

Standalone typed file-system operations and sources.

| Node | Input | Output value |
|------|-------|--------------|
| `FileReadNode` | `FileReadRequest` | `FileReadContent` with exact `FlowContent` |
| `FileWriteNode` | `FileContentWriteRequest` | `FileWriteResult` |
| `DirectoryEnumerateNode` | source | `DirectoryEntry` |
| `FileWatchNode` | source | `FileChange` |

Configured base paths confine descendants and reject existing symbolic-link or
reparse-point segments. Bounded reads stream at most the configured limit plus
one byte before returning an oversized-file error. Unlimited reads retain the
normal streaming behavior.

Expected IO, validation, confinement, and size failures become `FlowError` on
normal Output. Sources emit typed entries/changes. The package owns no watcher
or clock supplied by an external host beyond the node lifecycle documented by
its constructor.

## Composition

Install `FluxFlow.Components.FileSystem.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
