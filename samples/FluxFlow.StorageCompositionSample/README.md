# FluxFlow Storage Composition Sample

This sample shows how a host can compose `FluxFlow.Components.Storage` without
putting a concrete database inside the component package.

The sample runs three workflows against the same host-owned in-memory store:

1. `sample.storage-put-source -> storage.put -> sample.storage-result-sink`
2. `sample.storage-get-source -> storage.get -> sample.storage-result-sink`
3. `sample.storage-delete-source -> storage.delete -> sample.storage-result-sink`

The host provides `InMemoryStorageStore` through `UseSharedStore`. The reusable
package owns the node behavior and the host owns the concrete storage lifetime.

Run it with:

```powershell
dotnet run --project samples\FluxFlow.StorageCompositionSample\FluxFlow.StorageCompositionSample.csproj
```
