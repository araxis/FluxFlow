# FluxFlow.Components.Storage

Standalone storage operation nodes over a host-owned neutral store.

All four operation nodes implement `IFlowComponent`. For example,
`workflow.Add("put", new StoragePutNode(store, options))` adds the actual instance;
`Ports` reuses its existing canonical Input/Output/Events blocks. The store remains
caller-owned after graph shutdown/disposal. No creator or authoring package is
required for this direct Core path.

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

Attributes on storage requests, queries, records, and results are immutable
ordinal snapshots. Initializers accept normal dictionaries, copy and normalize
them once, and expose only `IReadOnlyDictionary<string, string>`; later changes
to the caller-owned dictionary cannot alter an operation or returned record.

The node never owns a store supplied by the host. FileSystem and SQL-file
adapter packages implement the neutral store boundary.

## Composition

Install `FluxFlow.Components.Storage.Composition` for optional runtime registration and authoring. Editor metadata is separate: install `FluxFlow.Components.Storage.Designer` and call `AddStorageDesign()` after runtime registration. This runtime package remains free of Composition, Designer, and Engine dependencies.
