namespace FluxFlow.Components.Storage.Contracts;

public interface IStorageStoreFactory
{
    ValueTask<StorageStoreLease> OpenAsync(
        StorageStoreContext context,
        CancellationToken cancellationToken = default);
}
