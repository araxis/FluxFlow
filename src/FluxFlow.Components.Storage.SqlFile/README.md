# FluxFlow.Components.Storage.SqlFile

Single-file SQL storage adapter for `FluxFlow.Components.Storage`.

This package does not add workflow nodes. It provides an `IStorageStore`
implementation and registration helpers for the existing storage nodes:

- `storage.put`
- `storage.get`
- `storage.query`
- `storage.delete`

## Register

```csharp
var options = new StorageComponentOptions()
    .UseSqlFileStorage(new SqlFileStorageStoreOptions
    {
        DatabasePath = "data/storage.db",
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
records should share a deterministic time source. `SqlFileStorageStore` also
accepts `SqlFileStorageStoreOptions.Clock` for direct store use or an
adapter-specific override.

Hosts that use keyed resources can register the backend factory directly:

```csharp
services.AddFluxFlowSqlFileStorageStoreFactory(
    "items-store",
    new SqlFileStorageStoreOptions
    {
        DatabasePath = "data/storage.db",
        DefaultCollection = "items"
    });
```

This registers a keyed `IStorageStoreFactory`. Storage composition can reference
the same key through the `store` resource and will open and release leases as
part of composed node lifetime.

## Behavior

- one record table per database file
- store, collection, and key primary key
- create, replace, and upsert write modes
- unsupported write mode values are rejected
- optimistic version checks through `ExpectedVersion`
- expiration honored by `storage.get` and `storage.query`
- query by collection, key prefix, attributes, stored time bounds, expiration,
  offset, and limit
- query expiration checks use one captured clock timestamp for database and
  in-memory filtering
- owned store lifetime when opened through `UseSqlFileStorage`
- per-open context for store name, default collection, and clock

The adapter is intended for single-machine workflows, service hosts, and desktop
apps that need a durable local store with stronger coordination than per-record
JSON files.

## Options

| Option | Purpose |
|--------|---------|
| `DatabasePath` | Required database file path. |
| `StoreName` | Optional fallback store name when the node does not set `store`. |
| `CreateDatabase` | Allows creating the database file when it does not exist. |
| `CreateDirectory` | Creates the parent directory when it does not exist. |
| `AllowAbsoluteDatabasePath` | Allows absolute database path values. |
| `MaxValueBytes` | Rejects values whose serialized JSON exceeds the limit. |
| `DefaultCollection` | Optional fallback collection. |
| `BusyTimeoutMilliseconds` | Wait time for a locked database before failing. |
| `Clock` | Optional direct-store time source override. |

`DatabasePath`, `StoreName`, and `DefaultCollection` are trimmed when assigned;
blank store names and default collections are treated as absent. `MaxValueBytes`
and `BusyTimeoutMilliseconds` must be greater than zero.

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
