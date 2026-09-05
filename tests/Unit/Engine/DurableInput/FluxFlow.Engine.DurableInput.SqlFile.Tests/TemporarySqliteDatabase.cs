namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

internal sealed class TemporarySqliteDatabase : IDisposable
{
    private TemporarySqliteDatabase(string directoryPath, string databasePath)
    {
        DirectoryPath = directoryPath;
        DatabasePath = databasePath;
    }

    public string DirectoryPath { get; }

    public string DatabasePath { get; }

    public static TemporarySqliteDatabase Create(string fileName = "durable-input.db")
    {
        var directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"fluxflow-durable-input-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return new TemporarySqliteDatabase(
            directoryPath,
            Path.Combine(directoryPath, fileName));
    }

    public SqlFileDurableInputStore CreateStore(
        bool createDatabase = true,
        bool createDirectory = true,
        TimeSpan? busyTimeout = null)
        => new(new SqlFileDurableInputStoreOptions
        {
            DatabasePath = DatabasePath,
            AllowAbsoluteDatabasePath = true,
            CreateDatabase = createDatabase,
            CreateDirectory = createDirectory,
            BusyTimeout = busyTimeout ?? SqlFileDurableInputStoreOptions.DefaultBusyTimeout
        });

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
