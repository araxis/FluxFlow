namespace FluxFlow.Engine.DurableInput.SqlFile;

/// <summary>
/// Temporary registration-time builder for SQL-file durable input settings.
/// </summary>
public sealed class SqlFileDurableInputStoreOptionsBuilder
{
    public string? DatabasePath { get; set; }

    public bool CreateDatabase { get; set; } = true;

    public bool CreateDirectory { get; set; } = true;

    public bool AllowAbsoluteDatabasePath { get; set; }

    public TimeSpan BusyTimeout { get; set; } = SqlFileDurableInputStoreOptions.DefaultBusyTimeout;

    internal SqlFileDurableInputStoreOptions Build()
    {
        var options = new SqlFileDurableInputStoreOptions
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
