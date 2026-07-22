# FluxFlow.Components.Storage

Standalone logical-storage nodes over a host-owned `IStorageStore`. The
canonical nodes carry exact `FlowContent` and return typed normal results; no
engine, backend, serializer, or resource factory is owned by this package.

## Canonical Nodes

| Node | Input | Output |
|------|-------|--------|
| `StoragePutNode` | `StorageContentPutRequest` | `FlowResult<StoragePutOutcome>` |
| `StorageGetNode` | `StorageGetRequest` | `FlowResult<StorageGetOutcome>` |
| `StorageQueryNode` | `StorageQueryRequest` | `FlowResult<StorageQueryOutcome>` |
| `StorageDeleteNode` | `StorageDeleteRequest` | `FlowResult<StorageDeleteOutcome>` |

Each node has one Input, one broadcast Output, Events, and Completion. There is
no universal Errors data port. Invalid requests, missing records, backend
failures, optimistic-write failures, and invalid stored content are ordinary
results with stable `StorageResultKinds` and `StorageErrorCodeNames`. A later
accepted input continues after an operation failure.

```csharp
IStorageStore store = ...; // opened and owned by the host
await using var put = new StoragePutNode(
    store,
    new StoragePutOptions { Collection = "items" });

var results = new BufferBlock<FlowMessage<FlowResult<StoragePutOutcome>>>();
put.Output.LinkTo(results);

var command = FlowMessage.Create(new StorageContentPutRequest
{
    Key = "invoice-42",
    Content = FlowContent.FromBytes(
        invoiceBytes,
        "application/json",
        "utf-8")
});

await put.Input.SendAsync(command);
var result = await results.ReceiveAsync();
// result.CorrelationId == command.CorrelationId
// result.CausationId == command.MessageId
```

`StorageContentPutRequest` requires an original byte representation. A
value-only `FlowContent` returns `storage.content_unavailable`; use an explicit
Serialization component before storage. Content type and encoding are stored
as metadata and no implicit decoding occurs on get or query.

The canonical layer writes a private versioned JSON-compatible envelope through
the existing `IStorageStore.Value` boundary. The built-in FileSystem and
SqlFile adapters round-trip that envelope without source changes. Custom stores
used by canonical nodes must preserve JSON object values and record metadata.
The envelope is not a workflow contract and should not be inspected by hosts.

## Operation Semantics

- Put supports `Upsert`, `Create`, `Replace`, expected version, expiry, and
  copied attributes. `EmitStoredRecord` controls whether the successful outcome
  includes the complete content record.
- Get returns `StorageGetFound`, `StorageGetNotFound`, or `StorageGetFailed` on
  the same Output. Missing is not an error.
- Query returns one outcome with `Count` and, when `EmitRecordsInResult` is
  enabled, an immutable snapshot of content records. Per-record fan-out belongs
  to an explicit downstream component.
- Delete always returns deleted or missing for an accepted command.

Result envelopes preserve correlation, trace, and headers, create a new message
identity, and set causation to the input message. Record attributes and query
record lists are copied on assignment. Exact content bytes are defensively
copied by `FlowContent`.

## Ownership

The host opens, registers, and disposes stores. Nodes borrow `IStorageStore` and
never dispose it. `IStorageStoreFactory`, `StorageStoreLease`, and
`StorageStoreContext` remain available for host-managed store creation. The
[FileSystem](../FluxFlow.Components.Storage.FileSystem) and
[SqlFile](../FluxFlow.Components.Storage.SqlFile) packages are concrete backend
adapters.

Pass a `TimeProvider` when deterministic result and event timestamps are
required. Supply the same clock through `StorageStoreContext.Clock` when backend
stored timestamps and expiration checks must use that time source.

## Migration From 4.x

The concise node names now identify the exact-content/result implementations.
Replace the temporary `FlowContentStoragePutNode`,
`FlowContentStorageGetNode`, `FlowContentStorageQueryNode`, and
`FlowContentStorageDeleteNode` names with the names in the table above.

The previous component-facing `StoragePutRequest`/`StorageResult` operations,
`StorageQueryResult`, numeric `StorageErrorCodes`, `Found`, `NotFound`,
`Records`, and `Errors` ports were removed. Expected operation failures use
stable string codes in normal `FlowResult<T>` values, and branching belongs on
result fields or explicit downstream components.

`IStorageStore`, `IStorageStoreFactory`, `StoragePutRequest`, `StorageRecord`,
and `StorageResult` remain supported adapter-boundary contracts. Their
`StorageRecord.Value` property remains `object?` so existing stores and the
built-in backend packages do not need to change.

## Composition

Workflow definition loading, keyed resource resolution, node construction, and
linking live in the optional `FluxFlow.Components.Storage.Composition` package.
The runtime package remains directly usable and has no Composition, Designer,
Hosting, backend-adapter, or Engine dependency.
