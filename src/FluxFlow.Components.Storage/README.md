# FluxFlow.Components.Storage

Reusable logical storage components for FluxFlow.

The package owns workflow storage node behavior and neutral contracts. Hosts
provide the concrete store through an adapter.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `storage.put` | `Input` -> `Result`, `Errors` | Stores or updates a logical record. |
| `storage.get` | `Input` -> `Result`, `Found`, `NotFound`, `Errors` | Reads a logical record and routes found/missing results. |
| `storage.query` | `Input` -> `Result`, `Records`, `Errors` | Queries records by collection, key prefix, attributes, time bounds, and limit. |
| `storage.delete` | `Input` -> `Result`, `Errors` | Deletes a logical record and reports whether it existed. |

## Store Ownership

The package does not include a concrete database. Register a store factory from
the host:

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterStorageComponents(options => options
        .UseSharedStore(context => new AppStorageStore(context.StoreName)));
```

Use `StorageStoreLease.Owned(store)` when the node should dispose the store.
Use `StorageStoreLease.Shared(store)` when the host owns the store lifetime.

The factory receives the node address, node type, store name, and default
collection through `StorageStoreContext`.

## Runtime Timing

Storage nodes use `SystemStorageClock` by default. Hosts can provide an
`IStorageClock` when tests, replay, or deterministic dashboards need stable
timestamps:

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterStorageComponents(options => options
        .UseClock(storageClock)
        .UseSharedStore(context => new AppStorageStore(context.StoreName)));
```

The configured clock is also available on `StorageStoreContext`, so backend
stores can use the same time source for stored records and expiration checks.

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
- `IStorageClock`
- `SystemStorageClock`
- `StorageStoreContext`
- `StorageStoreLease`

`StorageRecord.Value` is `object?` in this first slice. Hosts own
serialization and can compose this package with serialization or payload
components before storage.

Per-message store failures emit `FlowError` and later messages continue.
Startup fails clearly when the store cannot be opened.

## Design Metadata

This package exposes a package-owned `IComponentDesignMetadataProvider` for its
node types. Hosts can compose it through `ComponentDesignMetadataCatalog` to
populate palettes, editors, validation views, and documentation without
duplicating package descriptors.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
