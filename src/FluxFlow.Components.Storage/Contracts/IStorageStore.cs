namespace FluxFlow.Components.Storage.Contracts;

public interface IStorageStore
{
    Task<StorageRecord> PutAsync(
        StoragePutRequest request,
        CancellationToken cancellationToken = default);

    Task<StorageRecord?> GetAsync(
        StorageGetRequest request,
        CancellationToken cancellationToken = default);

    Task<StorageResult> DeleteAsync(
        StorageDeleteRequest request,
        CancellationToken cancellationToken = default);
}
