using FluxFlow.Components.Storage.Options;

namespace FluxFlow.Components.Storage.SqlFile;

public static class SqlFileStorageRegistrationExtensions
{
    public static StorageComponentOptions UseSqlFileStorage(
        this StorageComponentOptions options,
        string databasePath)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        return options.UseSqlFileStorage(new SqlFileStorageStoreOptions
        {
            DatabasePath = databasePath
        });
    }

    public static StorageComponentOptions UseSqlFileStorage(
        this StorageComponentOptions options,
        SqlFileStorageStoreOptions sqlFileOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sqlFileOptions);

        return options.UseStoreFactory(new SqlFileStorageStoreFactory(sqlFileOptions));
    }
}
