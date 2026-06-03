# FluxFlow.Components.Storage.FileSystem

File-system-backed storage adapter for `FluxFlow.Components.Storage`.

This package does not add workflow nodes. It provides an `IStorageStore`
implementation and registration helpers for the existing storage nodes:

- `storage.put`
- `storage.get`
- `storage.query`
- `storage.delete`

## Register

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterStorageComponents(options => options
        .UseFileSystemStorage(new FileSystemStorageStoreOptions
        {
            RootDirectory = "data/storage",
            DefaultCollection = "items"
        }));
```

Set `StorageComponentOptions.UseClock(...)` when node results and adapter
records should share a deterministic time source. `FileSystemStorageStore`
also accepts `FileSystemStorageStoreOptions.Clock` for direct store use or an
adapter-specific override.

## Behavior

- one JSON file per record
- hashed store, collection, and key paths
- create, replace, and upsert write modes
- optimistic version checks through `ExpectedVersion`
- expiration honored by `storage.get`
- query by collection, key prefix, attributes, stored time bounds, expiration,
  offset, and limit
- best-effort atomic writes through a temporary file then replace
- owned store lifetime when created through `UseFileSystemStorage`

The adapter is intended for single-machine workflows, samples, tests, and simple desktop
or service hosts. It does not claim cross-process write coordination in this
first version.

## Options

| Option | Purpose |
|--------|---------|
| `RootDirectory` | Required directory where records are stored. |
| `StoreName` | Optional fallback store name when the node does not set `store`. |
| `CreateDirectory` | Creates the root directory when it does not exist. |
| `AllowAbsoluteRootDirectory` | Allows absolute root directory values. |
| `MaxValueBytes` | Rejects values whose serialized JSON exceeds the limit. |
| `DefaultCollection` | Optional fallback collection. |
| `FlushOnWrite` | Flushes file contents before replacing the record file. |
| `Clock` | Optional direct-store time source override. |

The package persists only neutral `StorageRecord` data. Hosts that need exact
payload shaping should compose serialization or payload nodes before storage.
