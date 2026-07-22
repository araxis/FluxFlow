# FluxFlow.Components.Storage.Composition

Optional `FluxFlow.Composition` registration helpers for the canonical storage
nodes. Hosts provide a keyed `IStorageStore` or `IStorageStoreFactory` and may
provide a keyed `TimeProvider`; this package owns none of those resources.

## Canonical Registration

```csharp
registry
    .RegisterStoragePut()
    .RegisterStorageGet()
    .RegisterStorageQuery()
    .RegisterStorageDelete();
```

| Type | Canonical ports |
|------|-----------------|
| `storage.put` | `StorageContentPutRequest` Input, `FlowResult<StoragePutOutcome>` Output |
| `storage.get` | `StorageGetRequest` Input, `FlowResult<StorageGetOutcome>` Output |
| `storage.query` | `StorageQueryRequest` Input, `FlowResult<StorageQueryOutcome>` Output |
| `storage.delete` | `StorageDeleteRequest` Input, `FlowResult<StorageDeleteOutcome>` Output |

Every canonical node exposes Events and no universal Errors surface. Workflow
links can branch on `Kind`, `IsError`, `Error.Code`, `Value.Found`, or other
ordinary result fields.

## Flat Document

```json
{
  "Resources": {
    "Storage": {
      "Primary": {
        "Type": "host.storage-store"
      }
    }
  },
  "Workflows": {
    "OrderProcessing": {
      "BuildContent": {
        "Type": "serialize.json",
        "Output": "BuildPut.Input"
      },
      "BuildPut": {
        "Type": "storage.put-request",
        "collection": "orders",
        "key": "order-42",
        "Output": "Save.Input"
      },
      "Save": {
        "Type": "storage.put",
        "collection": "orders",
        "mode": "Upsert",
        "store": "Resources.Storage.Primary",
        "Output": ["HandleResult.Input", "Audit.Input"]
      },
      "HandleResult": {
        "Type": "storage.result"
      },
      "Audit": {
        "Type": "audit.result"
      }
    }
  }
}
```

`BuildContent`, `storage.put-request`, `storage.result`, and `audit.result` are
host example types. Composition does not insert mapping or serialization. A put
command must be created explicitly from upstream FlowContent. Resource addresses
are resolved by the host's application address framework.

Direct keyed stores remain host-owned. Factory leases are opened during
composition build and disposed with the composed node. The optional `clock`
resource controls deterministic result and diagnostic timestamps.

## Migration From 2.x

The 3.x adapter removes explicit typed compatibility registration methods.
Use the four canonical registrations above, route expected failures through
ordinary result fields, and replace `Found`, `NotFound`, and `Records` branch
ports with conditional links or explicit downstream components.

## Design Metadata

Hosts should compose this provider through `ComponentDesignMetadataCatalog`.
The canonical catalog adds the traced `Events` output and an optional semantic
`processing` profile picker, and omits legacy `name`, `boundedCapacity`,
`maxDegreeOfParallelism`, and `ensureOrdered` options from normal editing.
Default execution requires no processing profile; raw provider metadata retains
released declarations for compatibility.


`StorageComponentDesignMetadataProvider` describes canonical fixed ports,
option grouping/editor hints, and host-owned resource picker hints for `store`
and `clock`. Designer metadata does not create
stores, open factories, execute nodes, or own runtime state.
