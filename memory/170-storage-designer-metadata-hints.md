# Storage Designer Metadata Hints

Date: 2026-06-30

## Summary

Completed the Storage composition Designer metadata hint pass. The
`storage.put`, `storage.get`, `storage.query`, and `storage.delete` metadata now
carry richer option hints plus host-owned resource key patterns for the required
store and optional clock resources. The change is descriptive metadata only.

## Changes

- Added option section, importance, and editor hints to
  `StorageComponentDesignMetadataProvider`:
  - Collection hint for `collection`.
  - Write hint for `mode`.
  - Results hints for `emitStoredRecord`, `emitRecordsInResult`, and
    `emitMissingAsResult`.
  - Expiration hint for `includeExpired`.
  - Query hints for `offset` and `limit`.
  - Records hint for `emitRecordOutputs`.
  - Runtime hint for `boundedCapacity`.
  - Enum and boolean options omit editor hints because Designer has no precise
    editor attribute values for those option kinds.
- Added host-owned resource key patterns while preserving existing resource
  shape:
  - Required `store` resource: `storage-store:{name}`.
  - Optional `clock` resource: `clock:{name}`.
- Preserved store resolution, store-factory lease behavior, record
  read/write/query/delete behavior, expiration handling, branch outputs, ports,
  configuration binding, runtime behavior, renderer behavior, hot reload
  behavior, and engine dependency boundaries.
- Bumped `FluxFlow.Components.Storage.Composition` from `1.3.1` to `1.4.0`.
- Updated the package README, package release notes, top-level changelog, and
  focused metadata tests.

## Verification

- `dotnet test tests\FluxFlow.Components.Storage.Composition.Tests\FluxFlow.Components.Storage.Composition.Tests.csproj --no-restore -v minimal`
  - Passed: 20
- `dotnet test tests\FluxFlow.Components.Designer.Tests\FluxFlow.Components.Designer.Tests.csproj --no-restore -v minimal`
  - Passed: 93
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  - Passed: 84
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  - Passed with 0 warnings and 0 errors.
- `graphify update . --force`
  - Refreshed local graph output: 11841 nodes, 20095 edges, 1085 communities.
  - `graph.html` was skipped because the graph exceeds the local HTML
    visualization limit; `graphify-out/` remains excluded from git.

## Next

Keep any further package-family Designer metadata hint pass separately planned,
locally scoped, and locally committed. Sessions is the next likely
component-family candidate if the rollout continues.
