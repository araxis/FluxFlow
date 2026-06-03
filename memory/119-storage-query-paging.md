# 119 - Storage Query Paging

## Status

Published:

- `FluxFlow.Components.Storage` `0.4.0-alpha.1`
- `FluxFlow.Components.Storage.FileSystem` `0.3.0-alpha.1`
- `FluxFlow.Components.Storage.SqlFile` `0.3.0-alpha.1`

## Decision

Storage queries now have explicit offset paging as a shared contract instead of
leaving each store adapter to invent its own paging rules.

`StorageQueryMatcher` is the shared validation and match helper for in-memory,
file-backed, and single-file SQL-backed query behavior. Store adapters remain
responsible for applying `Offset` and `Limit` after matching.

## Scope

- Added `StorageQueryRequest.Offset`.
- Added `StorageQueryOptions.Offset`.
- Added shared query validation for negative offsets, invalid limits, and
  reversed time ranges.
- Reused shared matching for collection, key prefix, time range, expired record
  handling, and attributes.
- Updated file-backed and single-file SQL-backed adapters to honor offset.
- Added focused tests for configured node offsets and adapter-level offsets.

## Verification

- `dotnet test tests\FluxFlow.Components.Storage.Tests\FluxFlow.Components.Storage.Tests.csproj --configuration Release`
- `dotnet test tests\FluxFlow.Components.Storage.FileSystem.Tests\FluxFlow.Components.Storage.FileSystem.Tests.csproj --configuration Release`
- `dotnet test tests\FluxFlow.Components.Storage.SqlFile.Tests\FluxFlow.Components.Storage.SqlFile.Tests.csproj --configuration Release`
- `dotnet build FluxFlow.sln --configuration Release`
- `dotnet test FluxFlow.sln --configuration Release --no-build`
- Public-feed smoke returned `a-2:a-2` for file-backed and single-file
  SQL-backed offset queries.

Release workflow runs:

- Storage: `26902248355`
- File-backed adapter: `26902247722`
- Single-file SQL adapter: `26902245411`

## Next

Continue the component maturity backlog. Good candidates:

- Secrets, if the next host integration needs runtime value resolution.
- Storage adapter variants, if a concrete host needs another persistence style.
- Further storage query hardening, if sorting or cursor paging becomes needed.
