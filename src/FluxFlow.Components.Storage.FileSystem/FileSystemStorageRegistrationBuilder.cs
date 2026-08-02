namespace FluxFlow.Components.Storage.FileSystem;

public sealed class FileSystemStorageRegistrationBuilder
{
    public string? RootDirectory { get; set; }

    public bool CreateDirectory { get; set; } = true;

    public bool AllowAbsoluteRootDirectory { get; set; } = true;

    public long MaxValueBytes { get; set; } = 1_048_576;

    public string? DefaultCollection { get; set; }

    public bool FlushOnWrite { get; set; } = true;

    public TimeProvider? Clock { get; set; }

    internal FileSystemStorageStoreOptions CreateOptions(string storeName)
    {
        if (string.IsNullOrWhiteSpace(RootDirectory))
        {
            throw new InvalidOperationException(
                "File-system storage registration requires a root directory.");
        }

        if (MaxValueBytes <= 0)
        {
            throw new InvalidOperationException(
                "File-system storage max value bytes must be greater than zero.");
        }

        return new FileSystemStorageStoreOptions
        {
            RootDirectory = RootDirectory,
            StoreName = storeName,
            CreateDirectory = CreateDirectory,
            AllowAbsoluteRootDirectory = AllowAbsoluteRootDirectory,
            MaxValueBytes = MaxValueBytes,
            DefaultCollection = DefaultCollection,
            FlushOnWrite = FlushOnWrite,
            Clock = Clock
        };
    }
}
