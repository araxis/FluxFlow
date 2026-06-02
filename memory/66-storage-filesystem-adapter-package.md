# Storage FileSystem Adapter Package

Date: 2026-06-01

Implemented the first persisted storage adapter package.

## Scope

Package:

- `FluxFlow.Components.Storage.FileSystem`

Public shape:

- `FileSystemStorageStore`
- `FileSystemStorageStoreOptions`
- `FileSystemStorageStoreFactory`
- `UseFileSystemStorage(...)`

## Decisions

- Keep this package adapter-only; it adds no workflow nodes.
- Persist one JSON file per `StorageRecord`.
- Store records under hashed store, collection, and key paths.
- Keep root directory explicit and validated.
- Support create, replace, upsert, expected version checks, expiration-aware
  reads, and delete found/missing results.
- Use temporary-file replacement for writes.
- Do not claim cross-process write coordination in this first version.

## Verification

Added focused tests for:

- record persistence across store instances
- write modes and expected version checks
- expiration-aware reads
- found and missing delete results
- safe paths for collection and key values
- maximum serialized value size
- factory context defaults and owned leases
- option validation
- storage node registration through `UseFileSystemStorage(...)`

Query support for the file-system adapter was added later in
`75-storage-query-component.md`.
