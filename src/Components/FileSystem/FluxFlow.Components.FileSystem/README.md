# FluxFlow.Components.FileSystem

## Request conversion and migration

Read/write request deserialization is local to this package and uses System.Text.Json.
The operation receives the original FlowMessage alongside its typed request; no
materializer object is needed to configure the internal operation pipeline.

The public FileReadRequestMaterializer and FileContentWriteRequestMaterializer types
have been removed. Replace their InputShape access with FileSystemInputShapes.Read
or FileSystemInputShapes.Write in the Contracts namespace. Direct Materialize callers
can deserialize the request using their own JSON options/error policy, or submit a
FlowMessage to the node to use the FileSystem domain-error contract. This is a
source-breaking change; node constructors and ports are unchanged. Input shapes
remain metadata rather than runtime validators. Directory/watch sources are unchanged.

Standalone typed file-system operations and sources.

`FileReadNode` and `FileWriteNode` implement `IFlowComponent`: construct either and
pass it directly to `workflow.Add(name, instance)`. Their `Ports` property reuses
the existing canonical Input/Output/Events blocks. This does not require an
definition package or a component creator.

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

Install `FluxFlow.Components.FileSystem.Composition` for optional runtime registration and definition. This runtime package remains free of Composition, Designer, and Engine dependencies.
