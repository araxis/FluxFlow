# FluxFlow.Components.FileSystem

Standalone file system nodes for FluxFlow, built on
[FluxFlow.Nodes](../FluxFlow.Nodes/README.md). Every node is a self-contained TPL
Dataflow processor — `new` it up and `LinkTo` the next node. No engine, registry, or
runtime required. Every message travels as a `FlowMessage<T>` envelope, so a correlation
id flows from a request to its result without any node copying it by hand.

## Nodes

| Node | Kind | Shape | Purpose |
|------|------|-------|---------|
| `FileReadNode` | transform | `Input` (`FileReadRequest`) -> `Output` (`FileReadResult`) | Reads file content as text or bytes and emits a read result. |
| `FileWriteNode` | transform | `Input` (`FileWriteRequest`) -> `Output` (`FileWriteResult`) | Writes request content to a file and emits a write result. |
| `DirectoryEnumerateNode` | source | `Output` (`DirectoryEnumerateEntry`) | Enumerates files and/or directories from a configured directory, then completes. |
| `FileWatchNode` | source | `Output` (`FileWatchEvent`) | Emits file system change events from a watched directory until stopped. |

The transforms (`FileReadNode` / `FileWriteNode`) accept a `FlowMessage<TRequest>` on
`Input` and broadcast a `FlowMessage<TResult>` on `Output` carrying the same correlation
id. The sources (`DirectoryEnumerateNode` / `FileWatchNode`) begin producing once
`StartAsync` is called and mint a fresh correlation id per emitted item.

Every node also exposes broadcast `Errors` (`FlowError`) and `Events` (`FlowEvent`)
ports. Domain failures surface on `Errors` (carrying the in-flight correlation id for
transforms) and do not stop later messages from being processed; transforms emit a
success/failure note on `Events`, and the sources emit started/entry/changed/completed
notes there too.

## Construction

Each node takes its options and an optional `TimeProvider` (used for all result and
event timestamps) directly:

```csharp
await using var read = new FileReadNode(new FileReadOptions { BaseDirectory = "data" });
await using var write = new FileWriteNode(new FileWriteOptions { BaseDirectory = "data" });
await using var enumerate = new DirectoryEnumerateNode(new DirectoryEnumerateOptions
{
    Directory = "inbox",
    BaseDirectory = "data"
});
await using var watch = new FileWatchNode(new FileWatchOptions
{
    Directory = "inbox",
    BaseDirectory = "data"
});
```

Pass a `Microsoft.Extensions.Time.Testing.FakeTimeProvider` (or any `TimeProvider`) as
the second argument to make timestamps deterministic in tests.

Options validate at construction. Invalid capacities and size limits fail fast
with the corresponding node option name, while path policy failures remain
runtime diagnostics on each node's `Errors` port.

## Read / Write transforms

```csharp
await read.Input.SendAsync(FlowMessage.Create(new FileReadRequest
{
    Path = "logs/output.txt",
    ReadAs = FileReadMode.Text
}));
var result = await read.Output.ReceiveAsync(); // FlowMessage<FileReadResult>
```

Use `ReadAs = FileReadMode.Bytes` for raw bytes. Text reads use the request `Encoding`
when provided, otherwise the option `DefaultEncoding`.

```csharp
await write.Input.SendAsync(FlowMessage.Create(new FileWriteRequest
{
    Path = "logs/output.txt",
    Content = "hello",
    Mode = FileWriteMode.Overwrite,
    CreateDirectories = true
}));
```

`Bytes` can be used instead of `Content`. When both are set, `Bytes` wins. Supported
write modes are `Overwrite`, `Append`, and `CreateNew`; supported read modes are `Text`
and `Bytes`.

## Watch source

```csharp
var watch = new FileWatchNode(new FileWatchOptions
{
    Directory = "inbox",
    Filter = "*.json",
    IncludeSubdirectories = false,
    NotifyFilters = ["FileName", "LastWrite", "Size"],
    BaseDirectory = "data",
    InternalBufferSize = 8192,
    BoundedCapacity = 128
});
await watch.StartAsync();
// watch.Output emits FlowMessage<FileWatchEvent> per change; watch.Complete() stops it.
```

`FileWatchEvent` carries the changed path, directory, name, change type, and old
path/name for rename events. `InternalBufferSize` optionally sets the underlying watcher
buffer and must be between 4096 and 65536 bytes when set.

## Directory enumerate source

```csharp
var enumerate = new DirectoryEnumerateNode(new DirectoryEnumerateOptions
{
    Directory = "inbox",
    Filter = "*.json",
    IncludeSubdirectories = true,
    IncludeFiles = true,
    IncludeDirectories = false,
    MaxEntries = 1000,
    BaseDirectory = "data",
    BoundedCapacity = 128
});
await enumerate.StartAsync();
// enumerate.Output emits FlowMessage<DirectoryEnumerateEntry>, then completes.
```

`DirectoryEnumerateEntry` carries the resolved path, source directory, name, entry type,
optional byte length, timestamps, and file attributes.

## Path resolution

Relative paths are resolved under `BaseDirectory` when it is set. Relative paths that
escape the base directory are rejected. Absolute paths are rejected unless
`AllowAbsolutePaths` is true. When `BaseDirectory` is not set and `AllowAbsolutePaths`
is false, the current working directory is the implicit base and relative paths that
escape it are rejected.

`FileReadOptions.MaxBytes` defaults to 16777216 (16 MiB). Set it higher for larger
files, or set it explicitly to `null` to keep unlimited reads.

## Composition

Building a workflow, reading config, creating nodes, and linking them is a
separate concern from the node package. This package is just the standalone
nodes.

Use `FluxFlow.Components.FileSystem.Composition` when a
`FluxFlow.Composition` host should register optional file-system factories:

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterFileRead()
        .RegisterFileWrite()
        .RegisterDirectoryEnumerate()
        .RegisterFileWatch());
```

The composition adapter binds existing FileSystem option records from node
configuration and can resolve an optional keyed `TimeProvider` resource named
`clock`. Base-directory and absolute-path behavior remain normal node options;
the adapter does not add a separate path-resource or sandbox model.
