# FluxFlow.Components.FileSystem

Reusable file system components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `directory.enumerate` | `Output` | Emits files and directories from a configured directory. |
| `file.read` | `Input` -> `Result` | Reads file content as text or bytes and emits a read result. |
| `file.watch` | `Output` | Emits file system change events from a watched directory. |
| `file.write` | `Input` -> `Result` | Writes request content to a file and emits a write result. |

Failures emit `FlowError` through the node error stream and do not stop later
messages from being processed.

## Write Request

```csharp
new FileWriteRequest
{
    Path = "logs/output.txt",
    Content = "hello",
    Mode = FileWriteMode.Overwrite,
    CreateDirectories = true
}
```

`Bytes` can be used instead of `Content`. When both are set, `Bytes` wins.

## Read Request

```csharp
new FileReadRequest
{
    Path = "logs/output.txt",
    ReadAs = FileReadMode.Text
}
```

Use `ReadAs = FileReadMode.Bytes` when the workflow needs raw bytes. Text reads
use the request `Encoding` value when provided, otherwise `defaultEncoding`.

## Watch Output

```json
{
  "type": "file.watch",
  "directory": "inbox",
  "filter": "*.json",
  "includeSubdirectories": false,
  "notifyFilters": [ "FileName", "LastWrite", "Size" ],
  "baseDirectory": "data",
  "allowAbsolutePaths": false,
  "boundedCapacity": 128
}
```

`file.watch` emits `FileWatchEvent` values with the changed path, directory,
name, change type, and old path/name for rename events.

## Directory Enumerate Output

```json
{
  "type": "directory.enumerate",
  "directory": "inbox",
  "filter": "*.json",
  "includeSubdirectories": true,
  "includeFiles": true,
  "includeDirectories": false,
  "maxEntries": 1000,
  "baseDirectory": "data",
  "allowAbsolutePaths": false,
  "boundedCapacity": 128
}
```

`directory.enumerate` emits `DirectoryEnumerateEntry` values with the resolved
path, source directory, name, entry type, optional byte length, timestamps, and
file attributes.

## Configuration

```json
{
  "type": "file.read",
  "baseDirectory": "data",
  "allowAbsolutePaths": false,
  "defaultEncoding": "utf-8",
  "maxBytes": 1048576,
  "boundedCapacity": 128
}
```

Relative paths are resolved under `baseDirectory` when it is set. Relative
paths that escape the base directory are rejected. Absolute paths are rejected
unless `allowAbsolutePaths` is true.

Supported write modes:

- `Overwrite`
- `Append`
- `CreateNew`

Supported read modes:

- `Text`
- `Bytes`

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
