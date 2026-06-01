# Storage Composition Sample

Date: 2026-06-01

Added `samples/FluxFlow.StorageCompositionSample` to show package composition
for `FluxFlow.Components.Storage`.

## Scope

- Host-owned `InMemoryStorageStore` implements `IStorageStore`.
- The sample registers the store with `UseSharedStore`.
- Three small workflows run sequentially:
  - put two records through `storage.put`
  - read found and missing records through `storage.get`
  - delete found and missing records through `storage.delete`
- Host-owned source and sink nodes stay in the sample project.

## Decision

The sample keeps concrete persistence outside the package. The package owns
logical storage node behavior; the host owns the store implementation, store
lifetime, and any serialization choices.

## Verification

Run:

```powershell
dotnet build samples/FluxFlow.StorageCompositionSample/FluxFlow.StorageCompositionSample.csproj /nr:false
dotnet run --project samples/FluxFlow.StorageCompositionSample/FluxFlow.StorageCompositionSample.csproj --no-build
```
