# FluxFlow.Components.Storage

Standalone storage nodes for FluxFlow: blockified `put`/`get`/`query`/`delete`
over an injected `IStorageStore`. No engine required.

Each node is a self-contained TPL Dataflow processor built on
[`FluxFlow.Nodes`](../FluxFlow.Nodes). Every message travels as a
`FlowMessage<T>` envelope (payload + correlation id), so the correlation id
flows request -> result for free — and onto the `Found`/`NotFound` and `Records`
branches. The host owns the `IStorageStore` lifetime and injects the opened
store into each node; the nodes never open or dispose it (exactly like the HTTP
node over an `HttpClient`).

## Nodes

| Node | Shape | Purpose |
|------|-------|---------|
| `StoragePutNode` | `Input` -> `Output`, `Errors`, `Events` | Stores or updates a logical record. |
| `StorageGetNode` | `Input` -> `Output`, `Found`, `NotFound`, `Errors`, `Events` | Reads a logical record and fans found/missing results. |
| `StorageQueryNode` | `Input` -> `Output`, `Records`, `Errors`, `Events` | Queries records by collection, key prefix, attributes, time bounds, and limit. |
| `StorageDeleteNode` | `Input` -> `Output`, `Errors`, `Events` | Deletes a logical record and reports whether it existed. |

```csharp
// The host owns the store; the node just borrows it.
IStorageStore store = ...;
await using var put = new StoragePutNode(store);

var request = FlowMessage.Create(new StoragePutRequest
{
    Collection = "items",
    Key = "a",
    Value = "one"
});
await put.Input.SendAsync(request);

var result = await put.Output.ReceiveAsync();      // FlowMessage<StorageResult>
// result.CorrelationId == request.CorrelationId   // the envelope carries it
```

`Output`, `Found`, `NotFound`, `Records`, `Errors`, and `Events` are all broadcast
ports — link each to as many downstream consumers as you like.

## Put

```csharp
await using var put = new StoragePutNode(store, new StoragePutOptions
{
    Collection = "items",
    Mode = StorageWriteMode.Upsert,
    EmitStoredRecord = true,
    BoundedCapacity = 128
});
```

`StoragePutNode` consumes `StoragePutRequest` and emits `StorageResult`.
Supported modes are `Upsert`, `Create`, and `Replace`. The request can override
the node mode per item.

## Get

```csharp
await using var get = new StorageGetNode(store, new StorageGetOptions
{
    Collection = "items",
    IncludeExpired = false
});
```

`StorageGetNode` consumes `StorageGetRequest` and emits `StorageResult` on
`Output`. Found records are also fanned to `Found`; missing records are also
fanned to `NotFound`. A missing record is a normal result, not a processing
error.

## Query

```csharp
await using var query = new StorageQueryNode(store, new StorageQueryOptions
{
    Collection = "items",
    Offset = 0,
    Limit = 100,
    IncludeExpired = false,
    EmitRecordsInResult = true,
    EmitRecordOutputs = true
});
```

`StorageQueryNode` consumes `StorageQueryRequest` and emits one
`StorageQueryResult` on `Output`. The `Records` port emits each returned
`StorageRecord` (as a `FlowMessage<StorageRecord>`) when `EmitRecordOutputs` is
true. Requests can filter by collection, key prefix, exact-match attributes,
stored time bounds, expired-record policy, offset, and limit.

## Delete

```csharp
await using var delete = new StorageDeleteNode(store, new StorageDeleteOptions
{
    Collection = "items",
    EmitMissingAsResult = true
});
```

`StorageDeleteNode` consumes `StorageDeleteRequest` and emits `StorageResult`.
Missing deletes can be emitted as normal results or suppressed
(`EmitMissingAsResult`).

## Errors and events

A store failure or an invalid request surfaces a `FlowError` on `Errors`
(stamped with the in-flight correlation id and a `Code` from
`StorageErrorCodes`) and the node keeps processing later messages. Each node
also emits `FlowEvent` diagnostics on `Events` (names in
`StorageDiagnosticNames`).

## Store Ownership

The package does not include a concrete database. A host supplies an
`IStorageStore` — see the [FileSystem](../FluxFlow.Components.Storage.FileSystem)
and [SqlFile](../FluxFlow.Components.Storage.SqlFile) adapter packages — and
injects it into the nodes.

Adapter packages register a store factory through `StorageComponentOptions`:

```csharp
var options = new StorageComponentOptions()
    .UseFileSystemStorage("./data");      // adapter extension
IStorageStore store = (await options.StoreFactory
    .OpenAsync(new StorageStoreContext { StoreName = "items-db" })).Store;
```

`StorageStoreLease.Owned(store)` marks a store the lease should dispose;
`StorageStoreLease.Shared(store)` marks a host-owned store that must not be
disposed. The factory receives the store name, default collection, and clock
through `StorageStoreContext`. Delegate-backed factories registered through
`StorageComponentOptions.UseStore(...)` must return a non-null
`StorageStoreLease`; shared-store delegates must return a non-null store.
`StorageStoreContext` trims store names and default collections, treats blank
values as absent, and falls back to `TimeProvider.System` when a null clock is
assigned.

## Runtime Timing

Each node uses `System.TimeProvider` (default `TimeProvider.System`) for result
timestamps. Pass a deterministic `TimeProvider` (for example
`Microsoft.Extensions.Time.Testing.FakeTimeProvider`) when tests, replay, or
deterministic dashboards need stable timestamps:

```csharp
await using var put = new StoragePutNode(store, clock: fakeTimeProvider);
```

The same clock can be supplied to a backend store through
`StorageStoreContext.Clock` so stored records and expiration checks share one
time source.

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
- `StorageStoreContext`
- `StorageStoreLease`

`StorageRecord.Value` is `object?`: hosts own serialization and can compose this
package with serialization or payload components before storage.

Request contracts trim optional text fields such as collection, key prefix,
content type, and correlation id, treating blank values as absent. Attribute
dictionaries are copied on assignment, use ordinal key comparison, and treat
null as empty. Nodes and stores still own required collection/key validation so
invalid workflow messages surface as normal storage errors instead of
constructor failures.

Output contracts follow the same rule: records and results trim textual
identity/diagnostic fields, normalize blank optional values to absent, copy
attribute dictionaries with ordinal key comparison, and copy query result record
lists on assignment.

Node option records trim default collection names and treat blank collections as
absent. Invalid capacities, query paging values, and write modes are rejected
when options are assigned so direct-code and configuration-bound callers fail at
the component boundary.

## Composition

Building a workflow, reading config, creating nodes, and linking them is a
separate concern from the node package. This package is just the standalone
nodes and storage contracts.

Use `FluxFlow.Components.Storage.Composition` when a `FluxFlow.Composition`
host should register the optional storage factories:

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

The composition adapter binds the existing storage option records from node
configuration, resolves the required store from the keyed `store` resource, and
can resolve an optional keyed `TimeProvider` resource named `clock`. Concrete
store setup still belongs to the host or backend adapter packages; the
composition adapter only consumes an already registered `IStorageStore`.

The optional composition package also exposes
`StorageComponentDesignMetadataProvider` for neutral Designer metadata over the
storage composition node types. The standalone Storage package remains free of
Designer, Composition, and Engine dependencies.
