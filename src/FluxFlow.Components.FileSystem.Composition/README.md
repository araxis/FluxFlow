# FluxFlow.Components.FileSystem.Composition

Composition registration and Designer metadata for canonical file-system
transforms and sources. The adapter binds package options and an optional
host-owned keyed `TimeProvider`; it does not create file-system resources,
own path policy, decode content, or scan assemblies.

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
| `file.read` | `FlowContentFileReadNode` | `FileReadRequest` | `FlowResult<FileReadContent>` |
| `file.write` | `FlowContentFileWriteNode` | `FileContentWriteRequest` | `FlowResult<FileWriteResult>` |
| `directory.enumerate` | `FlowValueDirectoryEnumerateNode` | none | `FlowValue` |
| `file.watch` | `FlowValueFileWatchNode` | none | `FlowValue` |

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
        "Type": "directory.enumerate",
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
        "boundedCapacity": 128,
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

## Typed Compatibility

Register released typed contracts under distinct node types when needed:

```csharp
registry
    .RegisterFileReadResult("file.read.typed")
    .RegisterFileWriteResult("file.write.typed")
    .RegisterDirectoryEnumerateEntries("directory.enumerate.typed")
    .RegisterFileWatchEvents("file.watch.typed");
```

These explicit registrations retain the 1.x typed ports and Errors/Events
surfaces. Use distinct type names when canonical and compatibility factories
share a registry.

## Design Metadata

`FileSystemComponentDesignMetadataProvider` describes canonical fixed ports,
path/traversal/limit option hints, and the optional host-owned clock picker.
The canonical write metadata omits `defaultEncoding` because exact FlowContent
bytes are not encoded by the node. Metadata remains descriptive; hosts own UI,
persistence, validation display, resource resolution, and activation.
