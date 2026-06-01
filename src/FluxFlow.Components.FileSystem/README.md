# FluxFlow.Components.FileSystem

Reusable file system components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `file.write` | `Input` -> `Result` | Writes request content to a file and emits a write result. |

Failures emit `FlowError` through the node error stream and do not stop later
messages from being processed.

## Request

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

## Configuration

```json
{
  "type": "file.write",
  "baseDirectory": "data",
  "allowAbsolutePaths": false,
  "defaultEncoding": "utf-8",
  "boundedCapacity": 128
}
```

Relative paths are resolved under `baseDirectory` when it is set. Relative
paths that escape the base directory are rejected. Absolute paths are rejected
unless `allowAbsolutePaths` is true.

Supported modes:

- `Overwrite`
- `Append`
- `CreateNew`
