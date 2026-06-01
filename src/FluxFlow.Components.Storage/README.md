# FluxFlow.Components.Storage

Reusable logical storage components for FluxFlow.

The package owns workflow storage node behavior and neutral contracts. Hosts
provide the concrete store through an adapter.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `storage.put` | `Input` -> `Result`, `Errors` | Stores or updates a logical record. |
| `storage.get` | `Input` -> `Result`, `Found`, `NotFound`, `Errors` | Reads a logical record and routes found/missing results. |
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
- `StorageDeleteRequest`
- `StorageResult`
- `StorageRecord`
- `StorageWriteMode`
- `IStorageStore`
- `IStorageStoreFactory`
- `StorageStoreContext`
- `StorageStoreLease`

`StorageRecord.Value` is `object?` in this first slice. Hosts own
serialization and can compose this package with serialization or payload
components before storage.

Per-message store failures emit `FlowError` and later messages continue.
Startup fails clearly when the store cannot be opened.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
