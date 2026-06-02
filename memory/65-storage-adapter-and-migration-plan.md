# Storage Adapter And Migration Plan

Date: 2026-06-01

Recorded the next storage step after the base component package and composition
sample.

## Decision

Keep `FluxFlow.Components.Storage` as the logical workflow node package. Do not
add concrete persistence there.

Plan the first optional persisted adapter as:

```text
FluxFlow.Components.Storage.FileSystem
```

This adapter should provide a small file-system-backed `IStorageStore`
implementation and registration helpers. It should not add new node types.

## Why

The storage component package is now usable with any host-provided store. The
next useful artifact is a reusable adapter, but only if it stays neutral:

- no product workspace schema
- no dashboard contracts
- no scenario/test-runner concepts
- no application storage repositories
- no external protocol concepts

## Proposed Adapter Scope

Public shape:

- `FileSystemStorageStore`
- `FileSystemStorageStoreOptions`
- `FileSystemStorageStoreFactory`
- `UseFileSystemStorage(...)`

Core behavior:

- persist `StorageRecord` by collection and key
- support create, replace, and upsert
- support `ExpectedVersion`
- support expiration on read
- return found/missing delete results
- support owned and shared lifetimes
- validate root directory, collection, key, and capacity-related limits

Non-goals for v0.1:

- new workflow nodes
- query API
- cross-process write guarantees
- migrations
- distributed coordination
- host-specific storage schemas

## Host Migration Notes

For a host with existing internal storage nodes:

1. Keep existing node ids temporarily if existing app files depend on them.
2. Map host requests into storage package request contracts.
3. Implement `IStorageStore` over the current host store.
4. Register the storage package with that adapter.
5. Replace duplicated put/get/delete behavior with package nodes or a thin
   wrapper.
6. Keep catalog/editor defaults, logs, dashboard projection, and app metadata in
   the host.
7. Add regression tests around existing app files and lifecycle behavior.

## Next Action

Implement the adapter package only after choosing the persisted format and
single-process versus multi-process expectations.

The per-persistence-style adapter package rule was formalized later in
`76-storage-adapter-package-rule.md`.
