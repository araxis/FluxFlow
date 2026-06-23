# FluxFlow.Components.Storage.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone storage
nodes from `FluxFlow.Components.Storage`.

This package does not scan assemblies, open stores, own store leases, register
backend stores, or configure concrete storage adapters. Hosts register storage
node factories explicitly and provide a keyed `IStorageStore`; they may also
provide an optional keyed `TimeProvider`.

## Registration

```csharp
services.AddKeyedSingleton<IStorageStore>("items-store", store);

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterStoragePut()
        .RegisterStorageGet()
        .RegisterStorageQuery()
        .RegisterStorageDelete());
```

## Node Types

| Type | Node | Ports |
|------|------|-------|
| `storage.put` | `StoragePutNode` | `Input`, `Output` |
| `storage.get` | `StorageGetNode` | `Input`, `Output`, `Found`, `NotFound` |
| `storage.query` | `StorageQueryNode` | `Input`, `Output`, `Records` |
| `storage.delete` | `StorageDeleteNode` | `Input`, `Output` |

Each factory exposes `Events` and `Errors`. `store` is a required keyed
`IStorageStore` resource. `clock` is an optional keyed `TimeProvider` resource
for deterministic result, event, and error timestamps.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "put": {
              "type": "storage.put",
              "resources": {
                "store": "items-store",
                "clock": "fixed"
              },
              "configuration": {
                "collection": "items",
                "mode": "Upsert",
                "emitStoredRecord": true,
                "boundedCapacity": 128
              }
            }
          },
          "links": []
        }
      }
    }
  }
}
```

The adapter binds the existing storage option records from node configuration.
Backend packages such as file-system or SQL-file storage remain host concerns:
open or register the store in the host, then reference it from composition with
the `store` resource.

## Design Metadata

`StorageComponentDesignMetadataProvider` exposes neutral Designer metadata for
`storage.put`, `storage.get`, `storage.query`, and `storage.delete` so hosts can
build palettes, editors, validation hints, or documentation without copying
package descriptors. The metadata describes the existing storage option records
and fixed ports. Concrete `IStorageStore` instances and optional keyed
`TimeProvider` clocks remain host-owned resources and are not modeled as editable
node options.
