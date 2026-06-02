# Storage Clock Hardening

Date: 2026-06-02

## Goal

Make logical storage nodes and reusable storage adapters deterministic for
tests, replay, and dashboard projections without changing the storage data
contracts or introducing a concrete persistence dependency into the logical
storage package.

## Decision

Add a shared `IStorageClock` abstraction to `FluxFlow.Components.Storage`.

The logical storage package owns the clock contract because:

- storage nodes stamp emitted `StorageResult` and `StorageQueryResult`
  messages;
- store factories already receive `StorageStoreContext`;
- backend adapter packages can consume the shared context clock without adding
  adapter-specific clock abstractions;
- hosts can configure one time source for both node results and backend record
  timestamps.

`StorageStoreContext` now carries the configured clock. Existing host-created
contexts continue to work because the property defaults to
`SystemStorageClock.Instance`.

Backend adapter options also accept an optional `Clock` override for direct
store construction outside workflow node registration.

## Implemented

- Added `IStorageClock` and `SystemStorageClock`.
- Added `StorageComponentOptions.UseClock(...)`.
- Threaded the configured clock into `StorageStoreContext`.
- Updated `storage.put`, `storage.get`, and `storage.query` result timestamps
  to use the configured clock.
- Updated `FluxFlow.Components.Storage.FileSystem` to use the configured clock
  for:
  - `StorageRecord.StoredAt`;
  - delete result timestamps;
  - expiration checks.
- Updated `FluxFlow.Components.Storage.SqlFile` to use the configured clock for:
  - `StorageRecord.StoredAt`;
  - delete result timestamps;
  - SQL query expiration filtering;
  - in-memory expiration checks.
- Added focused tests for logical node result timestamps, direct adapter store
  timing, and context-clock propagation through registration.

## Version Plan

- `FluxFlow.Components.Storage` -> `0.3.0-alpha.1`
- `FluxFlow.Components.Storage.FileSystem` -> `0.2.0-alpha.1`
- `FluxFlow.Components.Storage.SqlFile` -> `0.2.0-alpha.1`

Each package remains independently released. The coordinated version update is
needed because the shared clock is in the logical package and both adapter
packages depend on the updated context behavior.

## Verification

- `dotnet test tests\FluxFlow.Components.Storage.Tests\FluxFlow.Components.Storage.Tests.csproj -c Release --no-restore`
- `dotnet test tests\FluxFlow.Components.Storage.FileSystem.Tests\FluxFlow.Components.Storage.FileSystem.Tests.csproj -c Release --no-restore`
- `dotnet test tests\FluxFlow.Components.Storage.SqlFile.Tests\FluxFlow.Components.Storage.SqlFile.Tests.csproj -c Release --no-restore`

The first focused pass passed for all three test projects. A full solution
build/test and package smoke check remain before publishing.
