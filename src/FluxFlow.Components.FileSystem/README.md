# FluxFlow.Components.FileSystem

Standalone file-system transforms and sources over `FluxFlow.Nodes`. The
canonical transforms preserve transported bytes as `FlowContent`; expected
operation failures are normal `FlowResult<T>` values. No engine, registry,
service provider, or file-system abstraction is required for direct use.

## Canonical Nodes

| Node | Kind | Input | Output |
|------|------|-------|--------|
| `FileReadNode` | transform | `FileReadRequest` | `FlowResult<FileReadContent>` |
| `FileWriteNode` | transform | `FileContentWriteRequest` | `FlowResult<FileWriteResult>` |
| `DirectoryEnumerateNode` | source | none | `FlowValue` |
| `FileWatchNode` | source | none | `FlowValue` |

Read and write expose one normal Output plus Events and no universal Errors
port. Path, size, content, access, and I/O outcomes use stable
`FileSystemResultKinds` and `FileSystemErrorCodeNames`. Later accepted inputs
continue after a failure result.

Directory enumeration and file watching remain zero-input sources. They emit
ordinary immutable workflow objects and report runtime source failures through
their isolated `Completion` task. Start them with `StartAsync`; stop a live
watch with `Complete` or disposal.

## Exact Content

```csharp
await using var read = new FileReadNode(new FileReadOptions
{
    BaseDirectory = "data",
    MaxBytes = 16_777_216
});

var request = FlowMessage.Create(new FileReadRequest
{
    Path = "inbox/order.json",
    ReadAs = FileReadMode.Text,
    ContentType = "application/json",
    Encoding = "utf-8"
});

await read.Input.SendAsync(request);
var readResult = await read.Output.ReceiveAsync();
var content = readResult.Payload.Value!.Content;
```

The read node always retains exact file bytes. `ReadAs.Text` records a
normalized encoding and defaults the content type to `text/plain` when none is
provided. `ReadAs.Bytes` defaults to `application/octet-stream` and does not
attach an encoding. Decoding belongs to Serialization or Mapping.

```csharp
await using var write = new FileWriteNode(new FileWriteOptions
{
    BaseDirectory = "data"
});

await write.Input.SendAsync(FlowMessage.Create(new FileContentWriteRequest
{
    Path = "archive/order.json",
    Content = content,
    Mode = FileWriteMode.CreateNew,
    CreateDirectories = true
}));
```

The write node requires `FlowContent` with an original byte representation and
writes those bytes without implicit encoding or serialization. Value-only
content returns `file.write.content_unavailable`; explicitly serialize it
upstream when bytes are required.

## Source Values

`DirectoryEnumerateNode` emits objects containing `enumeratedAt`,
`path`, `directory`, `name`, `entryType`, `length`, `createdAt`,
`lastModifiedAt`, and numeric `attributes`.

`FileWatchNode` emits objects containing `timestamp`, `path`,
`directory`, `name`, `changeType`, `oldPath`, and `oldName`. File watcher
callbacks remain nonblocking and bounded by the configured source capacity.

## Path And Read Policy

Relative paths resolve under `BaseDirectory`. Without an explicit base, the
current working directory is the trusted base. Confined paths reject escapes
and existing descendant symbolic links or reparse points. The configured base
itself is trusted. Absolute paths are rejected unless `AllowAbsolutePaths` is
true and are not subject to descendant confinement.

`FileReadOptions.MaxBytes` defaults to 16 MiB. Limited reads stream at most
`MaxBytes + 1` bytes, including when a file grows during the read. Set the
option to `null` only when unlimited reads are intentional.

## Migration From 4.x

The concise names now identify the canonical implementations. Replace
`FlowContentFileReadNode`, `FlowContentFileWriteNode`,
`FlowValueDirectoryEnumerateNode`, and `FlowValueFileWatchNode` with the names
in the table above. The older `FileReadResult`, `FileWriteRequest`, typed
directory/watch event contracts, and source `Errors` ports were removed.
Expected read/write failures are normal `FlowResult<T>` values; source
infrastructure failures are observed through `Completion`.

## Composition

`FluxFlow.Components.FileSystem.Composition` registers the canonical fixed
ports and Designer metadata. It can resolve an optional host-owned keyed
`TimeProvider`; it does not own path policy, file handles beyond an operation,
watch lifetime beyond the node, persistence, decoding, or serialization.
