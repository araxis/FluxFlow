using FluxFlow.Components.Storage.Options;

namespace FluxFlow.Components.Storage.Local;

public static class LocalStorageRegistrationExtensions
{
    public static StorageComponentOptions UseLocalStorage(
        this StorageComponentOptions options,
        string rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UseLocalStorage(new LocalStorageStoreOptions
        {
            RootDirectory = rootDirectory
        });
    }

    public static StorageComponentOptions UseLocalStorage(
        this StorageComponentOptions options,
        LocalStorageStoreOptions localOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(localOptions);

        return options.UseStoreFactory(new LocalStorageStoreFactory(localOptions));
    }
}
