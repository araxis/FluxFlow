namespace FluxFlow.Engine.DurableOutput.TSql;

/// <summary>
/// Temporary registration-time builder for T-SQL durable-output settings.
/// </summary>
public sealed class TSqlDurableOutputStoreOptionsBuilder
{
    public string? ConnectionString { get; set; }

    public TimeSpan CommandTimeout { get; set; } =
        TSqlDurableOutputStoreOptions.DefaultCommandTimeout;

    public TimeSpan SchemaLockTimeout { get; set; } =
        TSqlDurableOutputStoreOptions.DefaultSchemaLockTimeout;

    public int ConnectRetryCount { get; set; } = 1;

    public TimeSpan ConnectRetryInterval { get; set; } =
        TSqlDurableOutputStoreOptions.DefaultConnectRetryInterval;

    public TSqlDurableOutputSchemaManagement SchemaManagement { get; set; } =
        TSqlDurableOutputSchemaManagement.CreateOrMigrate;

    internal TSqlDurableOutputStoreOptions Build()
    {
        var options = new TSqlDurableOutputStoreOptions
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
