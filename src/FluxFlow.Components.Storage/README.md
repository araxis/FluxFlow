# FluxFlow.Components.Storage

Standalone storage operation nodes over a host-owned neutral store.

| Node | Input | Output value |
|------|-------|--------------|
| `StoragePutNode` | `StorageContentPutRequest` | `StoragePutOutcome` |
| `StorageGetNode` | `StorageGetRequest` | `StorageGetOutcome` |
| `StorageDeleteNode` | `StorageDeleteRequest` | `StorageDeleteOutcome` |
| `StorageQueryNode` | `StorageQueryRequest` | `StorageQueryOutcome` |

Exact record content uses `FlowContent`; adapters receive that typed value and
its deterministic versioned JSON representation preserves bytes and metadata.
Reads continue to accept records written with the earlier private envelope.
Valid not-found/conflict-style outcomes remain typed. Store, validation, or
serialization failure becomes `FlowError` on Output.

The node never owns a store supplied by the host. FileSystem and SQL-file
adapter packages implement the neutral store boundary.

## Composition

Install `FluxFlow.Components.Storage.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
