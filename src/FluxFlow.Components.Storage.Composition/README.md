# FluxFlow.Components.Storage.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone storage
nodes from `FluxFlow.Components.Storage`.

This package does not scan assemblies, register backend stores, or configure
concrete storage adapters. Hosts register storage node factories explicitly and
provide either a keyed `IStorageStore` or a keyed `IStorageStoreFactory`; they
may also provide an optional keyed `TimeProvider`.

## Registration

```csharp
services.AddKeyedSingleton<IStorageStoreFactory>("items-store", storeFactory);

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
`IStorageStore` or `IStorageStoreFactory` resource. Direct stores remain
host-owned. Factory leases are opened during composition build and disposed with
the composed node. `clock` is an optional keyed `TimeProvider` resource for
deterministic result, event, and error timestamps.

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
Backend packages remain host concerns: register an opened keyed store or a
keyed store factory in the host, then reference it from composition with the
`store` resource.

## Design Metadata

`StorageComponentDesignMetadataProvider` exposes neutral Designer metadata for
`storage.put`, `storage.get`, `storage.query`, and `storage.delete` so hosts can
build palettes, editors, validation hints, or documentation without copying
package descriptors. The metadata describes the existing storage option records
and fixed ports, plus resource hints for the required `store` and optional
`clock` resources. Concrete `IStorageStore` instances, `IStorageStoreFactory`
registrations, and optional keyed `TimeProvider` clocks remain host-owned
resources and are not modeled as editable node options.
