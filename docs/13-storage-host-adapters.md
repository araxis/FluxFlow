# Storage Host Adapters

`FluxFlow.Components.Storage` owns workflow nodes and neutral storage
contracts. It does not own a concrete database or persistence technology.

That split is intentional:

- The package owns `storage.put`, `storage.get`, `storage.query`, and
  `storage.delete`.
- The host owns where records live.
- Optional adapter packages can be added later when a storage implementation is
  useful across more than one host.

## When To Keep Storage In The Host

Keep the store implementation inside the host when:

- only one application needs it
- the schema is still changing quickly
- records must share an application database or transaction boundary
- application permissions, retention, or encryption rules are not stable yet
- the workflow node contract is useful, but persistence is product-specific

This is the current default. The host implements `IStorageStore`, registers it
through `UseSharedStore` or `UseStore`, and keeps all persistence details behind
that adapter.

## When To Extract An Adapter Package

Extract a separate adapter package only when the adapter has a stable reusable
shape. Good signals:

- two applications need the same persisted record behavior
- setup options are generic, not product-specific
- records can be stored using only `StorageRecord` and request contracts
- no dashboard, workspace, scenario, or UI model is required
- lifecycle and ownership rules are clear enough for package users

The adapter package should not add new workflow nodes. It should only provide a
store implementation and registration helpers for the existing storage nodes.

## Adapter Package Rule

Every reusable persistence implementation must live in its own package under
the storage adapter namespace:

```text
FluxFlow.Components.Storage.*
```

The base package stays fixed as the logical workflow package:

```text
FluxFlow.Components.Storage
```

It owns contracts, store abstractions, leases, node options, diagnostics, and
the `storage.put`, `storage.get`, `storage.query`, and `storage.delete` nodes.
It must not grow concrete persistence implementations.

Adapter packages own one persistence style each. They can provide:

- one `IStorageStore` implementation
- one `IStorageStoreFactory` implementation when useful
- registration helpers such as `UseLocalStorage(...)`
- adapter-specific options and validation
- adapter-specific tests and README content

Adapter packages should avoid adding workflow node types. If a persistence
adapter appears to require new nodes, first check whether the base storage
contracts need a neutral extension instead.

Use neutral adapter package suffixes that describe the persistence style rather
than one host application. Examples:

```text
FluxFlow.Components.Storage.Local
FluxFlow.Components.Storage.EmbeddedDocument
FluxFlow.Components.Storage.EmbeddedSql
FluxFlow.Components.Storage.ServerSql
```

This keeps consumers free to reference only the persistence style they need and
lets each adapter move on its own release cadence.

## First Adapter Package

Package:

```text
FluxFlow.Components.Storage.Local
```

Purpose:

- provide a small local persisted `IStorageStore`
- use the existing `storage.put`, `storage.get`, `storage.query`, and
  `storage.delete` nodes
- keep all host-specific app schema outside the package
- avoid forcing a server or product database

Expected public shape:

- `LocalStorageStore`
- `LocalStorageStoreOptions`
- `LocalStorageStoreFactory`
- `UseLocalStorage(...)` registration helper

Expected options:

- `rootDirectory`
- `storeName`
- `createDirectory`
- `allowAbsoluteRootDirectory`
- `maxValueBytes`
- `defaultCollection`
- `flushOnWrite`

The package uses `StorageStoreLease.Owned(...)` when it creates the store. Hosts
can still pass a shared `LocalStorageStore` through the base storage package
when they want to own the lifetime.

## Record Model

The adapter should persist only the neutral storage contracts:

- collection
- key
- value
- content type
- attributes
- version
- stored timestamp
- expiration timestamp
- correlation id

Query support should stay contract-shaped:

- collection
- key prefix
- exact-match attributes
- stored time bounds
- expired-record policy
- limit

For the local adapter, `StorageRecord.Value` can be serialized as a normal
object payload.
Hosts that need exact payload control should compose serialization nodes before
storage and store a string or byte-like value with a content type.

## Safety Rules

The first persisted adapter should be conservative:

- require an explicit root directory
- validate collection and key before building storage paths
- keep collection and key values out of raw path segments
- reject empty collection or key values
- preserve optimistic concurrency through `ExpectedVersion`
- honor create, replace, and upsert modes
- honor expiration on reads
- honor query limits
- keep writes ordered per node
- avoid cross-process guarantees unless explicitly implemented

If a host needs multi-process coordination, shared server storage, backup
policies, or encrypted-at-rest guarantees, it should provide its own adapter
until those requirements are stable enough to extract.

## Migration Pattern

For a host that already has internal storage nodes:

1. Keep existing host-facing node ids if app files already depend on them.
2. Map host request models into `StoragePutRequest`, `StorageGetRequest`,
   `StorageQueryRequest`, or `StorageDeleteRequest`.
3. Implement `IStorageStore` over the host persistence layer.
4. Register `FluxFlow.Components.Storage` with that store.
5. Replace internal node behavior with package nodes or a thin host wrapper.
6. Keep catalog, editor defaults, dashboard projection, and app logs in the
   host.
7. Add tests for old app files, found/missing routing, failures, and lifecycle.

The wrapper can be removed later if the host can expose the package node ids
directly. Keeping the wrapper first makes migration smaller and protects
existing app definitions.

## Baseline Acceptance

- no new workflow nodes
- no host product concepts
- no UI, dashboard, workspace, or scenario contracts
- store options are explicit and validated
- owned and shared lifetime paths are covered
- put/get/query/delete behavior matches the base storage package tests
- persistence survives process restart in focused tests
- corrupt or unreadable records fail clearly without crashing unrelated records
- README shows the host registration pattern
