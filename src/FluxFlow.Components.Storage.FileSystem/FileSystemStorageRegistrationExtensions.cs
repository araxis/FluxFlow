using FluxFlow.Components.Storage.Options;

namespace FluxFlow.Components.Storage.FileSystem;

public static class FileSystemStorageRegistrationExtensions
{
    public static StorageComponentOptions UseFileSystemStorage(
        this StorageComponentOptions options,
        string rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        return options.UseFileSystemStorage(new FileSystemStorageStoreOptions
        {
            RootDirectory = rootDirectory
        });
    }

    public static StorageComponentOptions UseFileSystemStorage(
        this StorageComponentOptions options,
        FileSystemStorageStoreOptions fileSystemOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSystemOptions);

        return options.UseStoreFactory(new FileSystemStorageStoreFactory(fileSystemOptions));
    }
}
