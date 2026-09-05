namespace FluxFlow.Engine.DurableOutput.SqlFile;

/// <summary>
/// Temporary registration-time builder for SQL-file durable-output settings.
/// </summary>
public sealed class SqlFileDurableOutputStoreOptionsBuilder
{
    public string? DatabasePath { get; set; }

    public bool CreateDatabase { get; set; } = true;

    public bool CreateDirectory { get; set; } = true;

    public bool AllowAbsoluteDatabasePath { get; set; }

    public TimeSpan BusyTimeout { get; set; } = SqlFileDurableOutputStoreOptions.DefaultBusyTimeout;

    internal SqlFileDurableOutputStoreOptions Build()
    {
        var options = new SqlFileDurableOutputStoreOptions
        {
            DatabasePath = DatabasePath,
            CreateDatabase = CreateDatabase,
            CreateDirectory = CreateDirectory,
            AllowAbsoluteDatabasePath = AllowAbsoluteDatabasePath,
            BusyTimeout = BusyTimeout
        };

        _ = options.Resolve();
        return options;
    }
}
