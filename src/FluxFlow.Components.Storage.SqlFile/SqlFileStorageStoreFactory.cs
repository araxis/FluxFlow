using FluxFlow.Components.Storage.Contracts;

namespace FluxFlow.Components.Storage.SqlFile;

public sealed class SqlFileStorageStoreFactory : IStorageStoreFactory
{
    private readonly SqlFileStorageStoreOptions _options;

    public SqlFileStorageStoreFactory(SqlFileStorageStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ValueTask<StorageStoreLease> OpenAsync(
        StorageStoreContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var store = new SqlFileStorageStore(_options, context);
        return ValueTask.FromResult(StorageStoreLease.Owned(store));
    }
}
