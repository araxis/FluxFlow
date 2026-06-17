# FluxFlow.Components.Storage

Reusable logical storage components for FluxFlow.

The package owns workflow storage node behavior and neutral contracts. Hosts
provide the concrete store through an adapter.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `storage.store` | resource | Owns the store lifecycle. Operation nodes reference it by name and borrow the opened store. |
| `storage.put` | `Input` -> `Result`, `Errors` | Stores or updates a logical record. |
| `storage.get` | `Input` -> `Result`, `Found`, `NotFound`, `Errors` | Reads a logical record and routes found/missing results. |
| `storage.query` | `Input` -> `Result`, `Records`, `Errors` | Queries records by collection, key prefix, attributes, time bounds, and limit. |
| `storage.delete` | `Input` -> `Result`, `Errors` | Deletes a logical record and reports whether it existed. |

## Store resource

The `storage.store` node owns the store. It is a resource: the operation nodes
do not open their own store, they borrow the opened store from a `storage.store`
by name.

```json
{
  "type": "storage.store",
  "name": "default",
  "storeName": "items-db"
}
```

Each operation node requires a `store` that names a `storage.store` resource.
The reference is mandatory.

```json
{
  "type": "storage.put",
  "name": "save",
  "store": "default",
  "collection": "items"
}
```

## Store Ownership

The package does not include a concrete database. Register a store factory from
the host:

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterStorageComponents(options => options
        .UseSharedStore(context => new AppStorageStore(context.StoreName)));
```

Use `StorageStoreLease.Owned(store)` when the `storage.store` resource should
dispose the store. Use `StorageStoreLease.Shared(store)` when the host owns the
store lifetime.

The factory receives the node address, node type, store name, and default
collection through `StorageStoreContext`.

## Opening (host-driven)

Opening the store is an explicit host decision: there is no auto-open or lazy
open. `StartAsync` on the `storage.store` resource is a no-op. The host opens and
closes the store through `IStorageStoreHandle.ConnectAsync` /
`DisconnectAsync` (named for cross-protocol consistency even though storage
"opens" a store):

```csharp
await store.ConnectAsync(cancellationToken);
// ... run the graph ...
await store.DisconnectAsync(cancellationToken);
```

Operation nodes borrow the opened store at call-time and never open or dispose
it. An operation sent before the store is opened is reported per message on the
`Errors` port rather than faulting the node.

## Runtime Timing

The package uses `System.TimeProvider` (default `TimeProvider.System`); there is
no bespoke storage clock interface. Hosts can provide a deterministic
`TimeProvider` when tests, replay, or deterministic dashboards need stable
timestamps:

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterStorageComponents(options => options
        .UseClock(storageClock)
        .UseSharedStore(context => new AppStorageStore(context.StoreName)));
```

The configured `TimeProvider` is also available on `StorageStoreContext.Clock`,
so backend stores can use the same time source for stored records and expiration
checks.

## Put

```json
{
  "type": "storage.put",
  "store": "default",
  "collection": "items",
  "mode": "upsert",
  "emitStoredRecord": true,
  "boundedCapacity": 128
}
```

`storage.put` consumes `StoragePutRequest` and emits `StorageResult`.
Supported modes are `upsert`, `create`, and `replace`. The request can override
the node mode per item.

## Get

```json
{
  "type": "storage.get",
  "store": "default",
  "collection": "items",
  "includeExpired": false,
  "boundedCapacity": 128
}
```

`storage.get` consumes `StorageGetRequest` and emits `StorageResult` on
`Result`. Found records are also routed to `Found`; missing records are also
routed to `NotFound`. Missing records are normal results, not processing
errors.

## Query

```json
{
  "type": "storage.query",
  "store": "default",
  "collection": "items",
  "offset": 0,
  "limit": 100,
  "includeExpired": false,
  "emitRecordsInResult": true,
  "emitRecordOutputs": true,
  "boundedCapacity": 128
}
```

`storage.query` consumes `StorageQueryRequest` and emits one
`StorageQueryResult` on `Result`. The `Records` port emits each returned
`StorageRecord` when `emitRecordOutputs` is true.

Requests can filter by collection, key prefix, exact-match attributes, stored
time bounds, expired-record policy, offset, and limit. Store failures emit
`FlowError` and the node continues processing later messages.

## Delete

```json
{
  "type": "storage.delete",
  "store": "default",
  "collection": "items",
  "emitMissingAsResult": true,
  "boundedCapacity": 128
}
```

`storage.delete` consumes `StorageDeleteRequest` and emits `StorageResult`.
Missing deletes can be emitted as normal results or suppressed.

## Contracts

Core contracts:

- `StoragePutRequest`
- `StorageGetRequest`
- `StorageQueryRequest`
- `StorageDeleteRequest`
- `StorageQueryResult`
- `StorageResult`
- `StorageRecord`
- `StorageWriteMode`
- `IStorageStore`
- `IStorageStoreFactory`
- `IStorageStoreHandle`
- `StorageStoreContext`
- `StorageStoreLease`

`StorageRecord.Value` is `object?` in this first slice. Hosts own
serialization and can compose this package with serialization or payload
components before storage.

Per-message store failures emit `FlowError` and later messages continue.
Opening the store fails clearly when the store cannot be opened.

## Design Metadata

This package exposes a package-owned `IComponentDesignMetadataProvider` for its
node types. Hosts can compose it through `ComponentDesignMetadataCatalog` to
populate palettes, editors, validation views, and documentation without
duplicating package descriptors.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
