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
var options = new StorageComponentOptions()
    .UseFileSystemStorage(new FileSystemStorageStoreOptions
    {
        RootDirectory = "data/storage",
        DefaultCollection = "items"
    });

await using var lease = await options.StoreFactory.OpenAsync(
    new StorageStoreContext
    {
        StoreName = "default",
        Collection = "items",
        Clock = options.Clock
    });

IStorageStore store = lease.Store;
```

Set `StorageComponentOptions.UseClock(...)` when node results and adapter
records should share a deterministic time source. `FileSystemStorageStore`
also accepts `FileSystemStorageStoreOptions.Clock` for direct store use or an
adapter-specific override.

Hosts that use keyed resources can register the backend factory directly:

```csharp
services.AddFluxFlowFileSystemStorageStoreFactory(
    "items-store",
    new FileSystemStorageStoreOptions
    {
        RootDirectory = "data/storage",
        DefaultCollection = "items"
    });
```

This registers a keyed `IStorageStoreFactory`. Storage composition can reference
the same key through the `store` resource and will open and release leases as
part of composed node lifetime.

## Behavior

- one JSON file per record
- hashed store, collection, and key paths
- create, replace, and upsert write modes
- unsupported write mode values are rejected
- optimistic version checks through `ExpectedVersion`
- expiration honored by `storage.get`
- query by collection, key prefix, attributes, stored time bounds, expiration,
  offset, and limit
- query expiration checks use one captured clock timestamp per query
- best-effort atomic writes through a temporary file then replace
- shared store leases when opened through `UseFileSystemStorage`; the factory
  caches stores by root, store name, default collection, and clock, comparing
  root paths with the operating system's path case-sensitivity

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

`RootDirectory`, `StoreName`, and `DefaultCollection` trim surrounding
whitespace when assigned. Blank store names and default collections are treated
as absent. `MaxValueBytes` must be greater than zero.

The package persists only neutral `StorageRecord` data. Hosts that need exact
payload shaping should compose serialization or payload nodes before storage.
Attribute keys and values are trimmed before persistence and query matching.
Blank attribute keys/values and duplicate attribute keys after trimming are
rejected so attribute filters stay deterministic.
Invalid query paging and stored time ranges where `StoredFrom` is later than
`StoredTo` are rejected through the shared storage query validation.

## Composition

This package does not expose `FluxFlow.Composition` node factories. Use
`FluxFlow.Components.Storage.Composition` for `storage.put`, `storage.get`,
`storage.query`, and `storage.delete`; register either an opened
`IStorageStore` or this package's keyed `IStorageStoreFactory` as a host-owned
resource for those factories.
