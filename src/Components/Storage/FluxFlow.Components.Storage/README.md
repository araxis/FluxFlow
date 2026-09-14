# FluxFlow.Components.Storage

Standalone storage operation nodes over a host-owned neutral store.

All four operation nodes implement `IFlowComponent`. For example,
`workflow.Add("put", new StoragePutNode(store, options))` adds the actual instance;
`Ports` reuses its existing canonical Input/Output/Events blocks. The store remains
caller-owned after graph shutdown/disposal. No creator or definition package is
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

## Request conversion and migration

Request deserialization uses component-local System.Text.Json options. Runtime
operations receive the original FlowMessage and a plain typed request; a separate
materializer object is not needed to construct or extend an operation.

The four public request materializer classes in the former `Materialization`
namespace have been removed. Code that read their `InputShape` should use
`StorageInputShapes.Put`, `.Get`, `.Query` or `.Delete` from the `Contracts` namespace.
Code that called `Materialize` directly should deserialize the appropriate request
with its own JSON options and error policy, or submit a FlowMessage to the node to
use Storage's domain errors. This is a source-breaking change; node constructors
and ports are unchanged. Shape descriptions are metadata, not a replacement parser.

## Composition

Install `FluxFlow.Components.Storage.Composition` for optional runtime registration and definition. This runtime package remains free of Composition, Designer, and Engine dependencies.
