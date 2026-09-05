namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

internal sealed class TemporarySqliteDatabase : IDisposable
{
    private TemporarySqliteDatabase(string directoryPath, string databasePath)
    {
        DirectoryPath = directoryPath;
        DatabasePath = databasePath;
    }

    public string DirectoryPath { get; }

    public string DatabasePath { get; }

    public static TemporarySqliteDatabase Create(string fileName = "durable-output.db")
    {
        var directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"fluxflow-durable-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return new TemporarySqliteDatabase(
            directoryPath,
            Path.Combine(directoryPath, fileName));
    }

    public SqlFileDurableOutputStore CreateStore(
        bool createDatabase = true,
        bool createDirectory = true,
        TimeSpan? busyTimeout = null)
        => new(new SqlFileDurableOutputStoreOptions
        {
            DatabasePath = DatabasePath,
            AllowAbsoluteDatabasePath = true,
            CreateDatabase = createDatabase,
            CreateDirectory = createDirectory,
            BusyTimeout = busyTimeout ?? SqlFileDurableOutputStoreOptions.DefaultBusyTimeout
        });

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
            Directory.Delete(DirectoryPath, recursive: true);
    }
}
