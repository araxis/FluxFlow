# FluxFlow.Components.FileSystem.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone file
system nodes from `FluxFlow.Components.FileSystem`.

This package does not scan assemblies, resolve CLR types from strings, create
file-system abstraction resources, or own path policy. Hosts register file
system factories explicitly and may provide an optional keyed `TimeProvider`.

## Registration

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterFileRead()
        .RegisterFileWrite()
        .RegisterDirectoryEnumerate()
        .RegisterFileWatch());
```

## Node Types

| Type | Node | Ports |
|------|------|-------|
| `file.read` | `FileReadNode` | `Input`, `Output` |
| `file.write` | `FileWriteNode` | `Input`, `Output` |
| `directory.enumerate` | `DirectoryEnumerateNode` | `Output` |
| `file.watch` | `FileWatchNode` | `Output` |

The factories expose `Events` and `Errors`. `clock` is an optional keyed
`TimeProvider` resource for deterministic result, event, and error timestamps.
Path safety is still configured through the existing node options such as
`baseDirectory` and `allowAbsolutePaths`.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "read": {
              "type": "file.read",
              "resources": {
                "clock": "fixed"
              },
              "configuration": {
                "baseDirectory": "data",
                "allowAbsolutePaths": false,
                "defaultEncoding": "utf-8",
                "maxBytes": 16777216,
                "boundedCapacity": 128
              }
            },
            "enumerate": {
              "type": "directory.enumerate",
              "configuration": {
                "directory": "inbox",
                "filter": "*.json",
                "includeFiles": true,
                "includeDirectories": false,
                "baseDirectory": "data"
              }
            }
          },
          "links": []
        }
      }
    }
  }
}
```

The adapter binds the existing FileSystem option records from composition
configuration. `CompositionRuntime.StartAsync()` starts `directory.enumerate`
and `file.watch`; normal runtime stop/dispose stops `file.watch`.
