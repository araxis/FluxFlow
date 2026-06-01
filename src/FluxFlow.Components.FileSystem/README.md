# FluxFlow.Components.FileSystem

Reusable file system components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `file.read` | `Input` -> `Result` | Reads file content as text or bytes and emits a read result. |
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
