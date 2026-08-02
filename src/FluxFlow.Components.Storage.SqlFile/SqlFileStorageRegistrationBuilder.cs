namespace FluxFlow.Components.Storage.SqlFile;

public sealed class SqlFileStorageRegistrationBuilder
{
    public string? DatabasePath { get; set; }

    public bool CreateDatabase { get; set; } = true;

    public bool CreateDirectory { get; set; } = true;

    public bool AllowAbsoluteDatabasePath { get; set; } = true;

    public long MaxValueBytes { get; set; } = 1_048_576;

    public string? DefaultCollection { get; set; }

    public int BusyTimeoutMilliseconds { get; set; } = 30_000;

    public TimeProvider? Clock { get; set; }

    internal SqlFileStorageStoreOptions CreateOptions(string storeName)
    {
        if (string.IsNullOrWhiteSpace(DatabasePath))
        {
            throw new InvalidOperationException(
                "SQL file storage registration requires a database path.");
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

        return new SqlFileStorageStoreOptions
        {
            DatabasePath = DatabasePath,
            StoreName = storeName,
            CreateDatabase = CreateDatabase,
            CreateDirectory = CreateDirectory,
            AllowAbsoluteDatabasePath = AllowAbsoluteDatabasePath,
            MaxValueBytes = MaxValueBytes,
            DefaultCollection = DefaultCollection,
            BusyTimeoutMilliseconds = BusyTimeoutMilliseconds,
            Clock = Clock
        };
    }
}
