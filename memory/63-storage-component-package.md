# Storage Component Package

Date: 2026-06-01

Implemented the first `FluxFlow.Components.Storage` package.

## Scope

Nodes:

- `storage.put`
- `storage.get`
- `storage.delete`

Contracts:

- `StoragePutRequest`
- `StorageGetRequest`
- `StorageDeleteRequest`
- `StorageResult`
- `StorageRecord`
- `StorageWriteMode`
- `IStorageStore`
- `IStorageStoreFactory`
- `StorageStoreContext`
- `StorageStoreLease`

## Decisions

- Keep concrete databases outside the package.
- Open stores during node startup so startup failures are clear.
- Use explicit store ownership through `StorageStoreLease`.
- Keep `StorageRecord.Value` as `object?` for the first slice.
- Let hosts own serialization before or inside their store adapter.
- Route missing `storage.get` requests to `NotFound` as normal results.
- Let `storage.delete` suppress missing results when configured.
- Emit per-message store failures as `FlowError` and continue later messages.

## Verification

Added focused tests for:

- put upsert behavior
- create-mode conflict continuation
- get found/not-found routing
- expired record handling
- delete found/missing behavior
- missing delete result suppression
- startup factory failure
- invalid options and invalid requests
- diagnostics
- registration
- store lease disposal, including faulted node disposal

`storage.query` was added later in `75-storage-query-component.md`.
