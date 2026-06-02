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

Completed verification:

- `dotnet test tests\FluxFlow.Components.Storage.SqlFile.Tests\FluxFlow.Components.Storage.SqlFile.Tests.csproj -c Release --no-restore`
  passed with 11 tests.
- `dotnet build FluxFlow.sln -c Release --no-restore` passed with 0 warnings.
- `dotnet test FluxFlow.sln -c Release --no-restore` passed.
- `dotnet pack src\FluxFlow.Components.Storage.SqlFile\FluxFlow.Components.Storage.SqlFile.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`
  created the package and symbol package.
- Commit: `57bd553` (`Add SQL file storage adapter`).
- Tag: `components-storage-sqlfile-v0.1.0-alpha.1`.
- Release workflow: `26830934341`, success.
- Branch CI workflow: `26830925299`, success.
- Public package restore/build smoke passed on attempt 10.
