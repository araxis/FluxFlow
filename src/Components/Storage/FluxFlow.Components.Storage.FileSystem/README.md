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

Hosts that use keyed resources can register the backend through one flat
builder callback:

```csharp
services.AddFluxFlowFileSystemStorage("items-store", storage =>
{
    storage.RootDirectory = "data/storage";
    storage.DefaultCollection = "items";
    storage.CreateDirectory = true;
    storage.AllowAbsoluteRootDirectory = false;
    storage.MaxValueBytes = 1_048_576;
    storage.FlushOnWrite = true;
});
```

The callback runs once during registration. Its temporary mutable builder is
validated and converted to an immutable `FileSystemStorageStoreOptions`
snapshot; neither the callback nor builder is retained in DI. Registration is
side-effect free and does not create or inspect the directory. The registration
name becomes the canonical store name, and the method registers a keyed
`IStorageStoreFactory` backed by `FileSystemStorageStoreFactory`.

Advanced hosts use standard keyed DI instead of additional adapter overloads:

```csharp
services.AddKeyedSingleton<IStorageStore>(
    "shared-store",
    (provider, _) => CreateSharedStore(provider));

services.AddKeyedSingleton<IStorageStoreFactory>(
    "custom-store",
    (provider, _) => new CustomStorageStoreFactory(
        provider.GetRequiredService<CustomStorageDependency>()));
```

Storage composition resolves a keyed direct store before a keyed factory. A
direct store is shared and host-owned; a factory lease is released with the
composed component. Exact keys should therefore match the resource address in
the application definition.

The standalone `StorageComponentOptions.UseFileSystemStorage(...)` path shown
above remains available when no service collection is involved.

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
keyed `IStorageStore` or this package's keyed `IStorageStoreFactory` as a
host-owned resource for those factories.
