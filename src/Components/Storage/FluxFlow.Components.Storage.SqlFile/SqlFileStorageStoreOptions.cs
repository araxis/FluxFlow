using FluxFlow.Components.Storage.Contracts;

namespace FluxFlow.Components.Storage.SqlFile;

public sealed record SqlFileStorageStoreOptions
{
    private string? _databasePath;
    private string? _storeName;
    private long _maxValueBytes = 1_048_576;
    private string? _defaultCollection;
    private int _busyTimeoutMilliseconds = 30_000;

    public string? DatabasePath
    {
        get => _databasePath;
        init => _databasePath = Normalize(value);
    }

    public string? StoreName
    {
        get => _storeName;
        init => _storeName = Normalize(value);
    }

    public bool CreateDatabase { get; init; } = true;
    public bool CreateDirectory { get; init; } = true;
    public bool AllowAbsoluteDatabasePath { get; init; } = true;

    public long MaxValueBytes
    {
        get => _maxValueBytes;
        init => _maxValueBytes = ValidatePositive(value, nameof(MaxValueBytes));
    }

    public string? DefaultCollection
    {
        get => _defaultCollection;
        init => _defaultCollection = Normalize(value);
    }

    public int BusyTimeoutMilliseconds
    {
        get => _busyTimeoutMilliseconds;
        init => _busyTimeoutMilliseconds = ValidatePositive(value, nameof(BusyTimeoutMilliseconds));
    }

    public TimeProvider? Clock { get; init; }

    internal SqlFileStorageStoreSettings Resolve(StorageStoreContext? context = null)
    {
        var databasePath = DatabasePath;
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
            Clock ?? context?.Clock ?? TimeProvider.System);
    }

    internal static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static T ValidatePositive<T>(T value, string paramName)
        where T : struct, IComparable<T>
    {
        if (value.CompareTo(default) <= 0)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                "SQL file storage numeric limits must be greater than zero.");
        }

        return value;
    }
}
