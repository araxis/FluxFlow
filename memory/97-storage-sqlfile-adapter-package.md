# Storage SqlFile Adapter Package

Date: 2026-06-02

`FluxFlow.Components.Storage.SqlFile` is the next storage adapter package after
`FluxFlow.Components.Storage.FileSystem`.

## Decision

Add a separate package for a single-file SQL persistence backend. The package is
adapter-only and does not add workflow nodes.

The public package and namespace use the neutral `SqlFile` suffix. The concrete
provider remains an implementation detail.

## Package Shape

- Package: `FluxFlow.Components.Storage.SqlFile`
- Store: `SqlFileStorageStore`
- Factory: `SqlFileStorageStoreFactory`
- Options: `SqlFileStorageStoreOptions`
- Registration helpers: `UseSqlFileStorage(...)`

## Behavior

- Uses the existing logical storage nodes from `FluxFlow.Components.Storage`.
- Persists neutral `StorageRecord` data in a single database file.
- Supports put, get, query, and delete.
- Supports create, replace, and upsert write modes.
- Supports optimistic version checks through `ExpectedVersion`.
- Honors expiration on get/query unless the request includes expired records.
- Supports store and collection defaults from `StorageStoreContext`.

## Boundary

Hosts compose this adapter into storage nodes through
`RegisterStorageComponents`. Hosts still own path selection, resource naming,
and any higher-level retention or migration policy.

## Verification

Planned verification:

- Focused SQL file adapter tests.
- Full solution build and test pass.
- Package pack and public package smoke after release.
