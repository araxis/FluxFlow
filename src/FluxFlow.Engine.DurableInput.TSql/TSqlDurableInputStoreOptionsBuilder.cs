namespace FluxFlow.Engine.DurableInput.TSql;

/// <summary>
/// Temporary registration-time builder for T-SQL durable-input settings.
/// </summary>
public sealed class TSqlDurableInputStoreOptionsBuilder
{
    public string? ConnectionString { get; set; }

    public TimeSpan CommandTimeout { get; set; } =
        TSqlDurableInputStoreOptions.DefaultCommandTimeout;

    public TimeSpan SchemaLockTimeout { get; set; } =
        TSqlDurableInputStoreOptions.DefaultSchemaLockTimeout;

    public int ConnectRetryCount { get; set; } = 1;

    public TimeSpan ConnectRetryInterval { get; set; } =
        TSqlDurableInputStoreOptions.DefaultConnectRetryInterval;

    public TSqlDurableInputSchemaManagement SchemaManagement { get; set; } =
        TSqlDurableInputSchemaManagement.CreateOrMigrate;

    internal TSqlDurableInputStoreOptions Build()
    {
        var options = new TSqlDurableInputStoreOptions
        {
            ConnectionString = ConnectionString,
            CommandTimeout = CommandTimeout,
            SchemaLockTimeout = SchemaLockTimeout,
            ConnectRetryCount = ConnectRetryCount,
            ConnectRetryInterval = ConnectRetryInterval,
            SchemaManagement = SchemaManagement
        };
        var resolved = options.Resolve();
        return options with { ConnectionString = resolved.NormalizedConnectionString };
    }
}
