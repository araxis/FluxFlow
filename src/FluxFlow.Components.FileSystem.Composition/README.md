# FluxFlow.Components.FileSystem.Composition

Composition registration and Designer metadata for canonical file-system
transforms and sources. The adapter binds package options and an optional
host-owned keyed `TimeProvider`; it does not create file-system resources,
own path policy, decode content, or scan assemblies.

Existing definitions using `directory.enumerate` remain supported as a hidden
alias; new definitions and Designer palettes use `directory.list`.

## Canonical Registration

```csharp
registry
    .RegisterFileRead()
    .RegisterFileWrite()
    .RegisterDirectoryEnumerate()
    .RegisterFileWatch();
```

| Type | Node | Input | Output |
|------|------|-------|--------|
| `file.read` | `FileReadNode` | `FileReadRequest` | `FlowResult<FileReadContent>` |
| `file.write` | `FileWriteNode` | `FileContentWriteRequest` | `FlowResult<FileWriteResult>` |
| `directory.list` | `DirectoryEnumerateNode` | none | `FlowValue` |
| `file.watch` | `FileWatchNode` | none | `FlowValue` |

Canonical descriptors expose Events and no universal Errors surface. Expected
read/write failures are normal Output values. Directory and watch failures are
isolated source completion faults observed by the runtime.

## Flat Definition

```json
{
  "Resources": {
    "Shared": {
      "Clock": {
        "Type": "host.clock"
      }
    }
  },
  "Workflows": {
    "FileProcessing": {
      "Enumerate": {
        "Type": "directory.list",
        "directory": "inbox",
        "filter": "*.json",
        "includeFiles": true,
        "includeDirectories": false,
        "baseDirectory": "data",
        "clock": "Resources.Shared.Clock",
        "Output": "BuildRequest.Input"
      },
      "BuildRequest": {
        "Type": "file.read-request",
        "Output": "Read.Input"
      },
      "Read": {
        "Type": "file.read",
        "baseDirectory": "data",
        "maxBytes": 16777216,
        "Output": ["Handle.Input", "Audit.Input"]
      },
      "Handle": {
        "Type": "file.result"
      },
      "Audit": {
        "Type": "audit.result"
      }
    }
  }
}
```

The host example types are not supplied by this package. `BuildRequest`
represents the explicit conversion from the directory entry object to
`FileReadRequest`; Composition does not insert that conversion. Links can
branch on `IsError`, `Kind`, or
`Error.Code` without a special error edge.

`CompositionRuntime.StartAsync()` starts directory and watch sources. Normal
runtime stop or disposal stops a live watcher. Invalid options fail activation
through the node factory.

## Migration From 2.x

The 3.x adapter removes the explicit typed compatibility registration methods.
Use the four canonical registrations above, route expected read/write failures
as normal Output values, and observe directory/watch infrastructure failures
through component Completion.

## Design Metadata

Hosts should compose this provider through `ComponentDesignMetadataCatalog`.
The canonical catalog adds the traced `Events` output and an optional semantic
`processing` profile picker, and omits legacy `name`, `boundedCapacity`,
`maxDegreeOfParallelism`, and `ensureOrdered` options from normal editing.
Default execution requires no processing profile; raw provider metadata retains
released declarations for compatibility.


`FileSystemComponentDesignMetadataProvider` describes canonical fixed ports,
path/traversal/limit option hints, and the optional host-owned clock picker.
The canonical write metadata omits `defaultEncoding` because exact FlowContent
bytes are not encoded by the node. Metadata remains descriptive; hosts own UI,
persistence, validation display, resource resolution, and activation.
