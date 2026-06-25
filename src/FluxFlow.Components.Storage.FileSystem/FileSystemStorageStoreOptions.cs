using FluxFlow.Components.Storage.Contracts;
using System.IO;

namespace FluxFlow.Components.Storage.FileSystem;

public sealed record FileSystemStorageStoreOptions
{
    private string? _rootDirectory;
    private string? _storeName;
    private long _maxValueBytes = 1_048_576;
    private string? _defaultCollection;

    public string? RootDirectory
    {
        get => _rootDirectory;
        init => _rootDirectory = Normalize(value);
    }

    public string? StoreName
    {
        get => _storeName;
        init => _storeName = Normalize(value);
    }

    public bool CreateDirectory { get; init; } = true;
    public bool AllowAbsoluteRootDirectory { get; init; } = true;
    public long MaxValueBytes
    {
        get => _maxValueBytes;
        init => _maxValueBytes = ValidateMaxValueBytes(value);
    }

    public string? DefaultCollection
    {
        get => _defaultCollection;
        init => _defaultCollection = Normalize(value);
    }

    public bool FlushOnWrite { get; init; } = true;
    public TimeProvider? Clock { get; init; }

    internal FileSystemStorageStoreSettings Resolve(StorageStoreContext? context = null)
    {
        var rootDirectory = Normalize(RootDirectory);
        if (rootDirectory is null)
        {
            throw new InvalidOperationException(
                "File-system storage requires a root directory.");
        }

        if (Path.IsPathRooted(rootDirectory) && !AllowAbsoluteRootDirectory)
        {
            throw new InvalidOperationException(
                "File-system storage root directory cannot be absolute when absolute roots are disabled.");
        }

        var fullRoot = Path.GetFullPath(rootDirectory);
        if (Directory.Exists(fullRoot))
        {
            return CreateSettings(fullRoot, context);
        }

        if (!CreateDirectory)
        {
            throw new DirectoryNotFoundException(
                $"File-system storage root directory '{fullRoot}' does not exist.");
        }

        Directory.CreateDirectory(fullRoot);
        return CreateSettings(fullRoot, context);
    }

    private FileSystemStorageStoreSettings CreateSettings(
        string fullRoot,
        StorageStoreContext? context)
        => new(
            fullRoot,
            Normalize(context?.StoreName) ?? Normalize(StoreName) ?? "default",
            Normalize(context?.Collection) ?? Normalize(DefaultCollection),
            MaxValueBytes,
            FlushOnWrite,
            Clock ?? context?.Clock ?? TimeProvider.System);

    internal static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static long ValidateMaxValueBytes(long value)
        => value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "File-system storage max value bytes must be greater than zero.");
}
