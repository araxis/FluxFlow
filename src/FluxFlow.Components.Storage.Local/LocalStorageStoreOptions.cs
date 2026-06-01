using FluxFlow.Components.Storage.Contracts;
using System.IO;

namespace FluxFlow.Components.Storage.Local;

public sealed record LocalStorageStoreOptions
{
    public string? RootDirectory { get; init; }
    public string? StoreName { get; init; }
    public bool CreateDirectory { get; init; } = true;
    public bool AllowAbsoluteRootDirectory { get; init; } = true;
    public long MaxValueBytes { get; init; } = 1_048_576;
    public string? DefaultCollection { get; init; }
    public bool FlushOnWrite { get; init; } = true;

    internal LocalStorageStoreSettings Resolve(StorageStoreContext? context = null)
    {
        var rootDirectory = Normalize(RootDirectory);
        if (rootDirectory is null)
        {
            throw new InvalidOperationException(
                "Local storage requires a root directory.");
        }

        if (Path.IsPathRooted(rootDirectory) && !AllowAbsoluteRootDirectory)
        {
            throw new InvalidOperationException(
                "Local storage root directory cannot be absolute when absolute roots are disabled.");
        }

        if (MaxValueBytes <= 0)
        {
            throw new InvalidOperationException(
                "Local storage max value bytes must be greater than zero.");
        }

        var fullRoot = Path.GetFullPath(rootDirectory);
        if (Directory.Exists(fullRoot))
        {
            return CreateSettings(fullRoot, context);
        }

        if (!CreateDirectory)
        {
            throw new DirectoryNotFoundException(
                $"Local storage root directory '{fullRoot}' does not exist.");
        }

        Directory.CreateDirectory(fullRoot);
        return CreateSettings(fullRoot, context);
    }

    private LocalStorageStoreSettings CreateSettings(
        string fullRoot,
        StorageStoreContext? context)
        => new(
            fullRoot,
            Normalize(context?.StoreName) ?? Normalize(StoreName) ?? "default",
            Normalize(context?.Collection) ?? Normalize(DefaultCollection),
            MaxValueBytes,
            FlushOnWrite);

    internal static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
