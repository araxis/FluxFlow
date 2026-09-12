# FluxFlow.Components.FileSystem

Standalone typed file-system operations and sources.

`FileReadNode` and `FileWriteNode` implement `IFlowComponent`: construct either and
pass it directly to `workflow.Add(name, instance)`. Their `Ports` property reuses
the existing canonical Input/Output/Events blocks. This does not require an
authoring package or a component creator.

Directory enumeration and file watching also add directly as instances. They
inherit the shared `FlowSource` lifecycle and expose only Output/Events, without
dummy input ports or a separate source pipeline. Configure and link before
calling `StartAsync()` (or let `FlowApplication.StartAsync()` start them).
Stop ends production; fault interrupts backpressured delivery. The watcher is
created at startup and released when its production task exits.

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

Install `FluxFlow.Components.FileSystem.Composition` for optional runtime registration and authoring. Editor metadata is separate: install `FluxFlow.Components.FileSystem.Designer` and call `AddFileSystemDesign()` after runtime registration. This runtime package remains free of Composition, Designer, and Engine dependencies.
