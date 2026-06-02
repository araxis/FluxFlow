using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Timing;

namespace FluxFlow.Components.Storage.SqlFile;

public sealed record SqlFileStorageStoreOptions
{
    public string? DatabasePath { get; init; }
    public string? StoreName { get; init; }
    public bool CreateDatabase { get; init; } = true;
    public bool CreateDirectory { get; init; } = true;
    public bool AllowAbsoluteDatabasePath { get; init; } = true;
    public long MaxValueBytes { get; init; } = 1_048_576;
    public string? DefaultCollection { get; init; }
    public int BusyTimeoutMilliseconds { get; init; } = 30_000;
    public IStorageClock? Clock { get; init; }

    internal SqlFileStorageStoreSettings Resolve(StorageStoreContext? context = null)
    {
        var databasePath = Normalize(DatabasePath);
        if (databasePath is null)
        {
            throw new InvalidOperationException(
                "SQL file storage requires a database path.");
        }

        if (Path.IsPathRooted(databasePath) && !AllowAbsoluteDatabasePath)
        {
            throw new InvalidOperationException(
                "SQL file storage database path cannot be absolute when absolute paths are disabled.");
        }

        if (MaxValueBytes <= 0)
        {
            throw new InvalidOperationException(
                "SQL file storage max value bytes must be greater than zero.");
        }

        if (BusyTimeoutMilliseconds <= 0)
        {
            throw new InvalidOperationException(
                "SQL file storage busy timeout must be greater than zero.");
        }

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            if (!CreateDirectory)
            {
                throw new DirectoryNotFoundException(
                    $"SQL file storage directory '{directory}' does not exist.");
            }

            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(fullPath) && !CreateDatabase)
        {
            throw new FileNotFoundException(
                $"SQL file storage database '{fullPath}' does not exist.",
                fullPath);
        }

        return new SqlFileStorageStoreSettings(
            fullPath,
            Normalize(context?.StoreName) ?? Normalize(StoreName) ?? "default",
            Normalize(context?.Collection) ?? Normalize(DefaultCollection),
            MaxValueBytes,
            BusyTimeoutMilliseconds,
            CreateDatabase,
            Clock ?? context?.Clock ?? SystemStorageClock.Instance);
    }

    internal static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
