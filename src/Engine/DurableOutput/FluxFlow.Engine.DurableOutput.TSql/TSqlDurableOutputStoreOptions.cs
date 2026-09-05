using Microsoft.Data.SqlClient;

namespace FluxFlow.Engine.DurableOutput.TSql;

/// <summary>
/// Immutable configuration for <see cref="TSqlDurableOutputStore"/>.
/// </summary>
public sealed record TSqlDurableOutputStoreOptions
{
    public static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultSchemaLockTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultConnectRetryInterval = TimeSpan.FromSeconds(1);

    private string? _connectionString;
    private TimeSpan _commandTimeout = DefaultCommandTimeout;
    private TimeSpan _schemaLockTimeout = DefaultSchemaLockTimeout;
    private int _connectRetryCount = 1;
    private TimeSpan _connectRetryInterval = DefaultConnectRetryInterval;
    private TSqlDurableOutputSchemaManagement _schemaManagement =
        TSqlDurableOutputSchemaManagement.CreateOrMigrate;

    public string? ConnectionString
    {
        get => _connectionString;
        init => _connectionString = Normalize(value);
    }

    public TimeSpan CommandTimeout
    {
        get => _commandTimeout;
        init => _commandTimeout = ValidateWholeSeconds(
            value,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(10),
            nameof(CommandTimeout));
    }

    public TimeSpan SchemaLockTimeout
    {
        get => _schemaLockTimeout;
        init => _schemaLockTimeout = ValidateWholeMilliseconds(
            value,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(10),
            nameof(SchemaLockTimeout));
    }

    public int ConnectRetryCount
    {
        get => _connectRetryCount;
        init => _connectRetryCount = value is >= 0 and <= 5
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(ConnectRetryCount),
                value,
                "T-SQL durable output connection retry count must be between zero and five.");
    }

    public TimeSpan ConnectRetryInterval
    {
        get => _connectRetryInterval;
        init => _connectRetryInterval = ValidateWholeSeconds(
            value,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(60),
            nameof(ConnectRetryInterval));
    }

    public TSqlDurableOutputSchemaManagement SchemaManagement
    {
        get => _schemaManagement;
        init => _schemaManagement = Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(SchemaManagement),
                value,
                "T-SQL durable output schema management mode is invalid.");
    }

    public override string ToString()
        => $"{nameof(TSqlDurableOutputStoreOptions)} {{ ConnectionString = [redacted], " +
           $"CommandTimeout = {CommandTimeout}, SchemaLockTimeout = {SchemaLockTimeout}, " +
           $"ConnectRetryCount = {ConnectRetryCount}, ConnectRetryInterval = {ConnectRetryInterval}, " +
           $"SchemaManagement = {SchemaManagement} }}";

    internal TSqlDurableOutputStoreSettings Resolve()
    {
        var connectionString = ConnectionString ?? throw new InvalidOperationException(
            "T-SQL durable output requires a connection string.");

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new ArgumentException(
                "T-SQL durable output connection string is invalid.",
                nameof(ConnectionString),
                exception);
        }

        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new ArgumentException(
                "T-SQL durable output connection string must specify a server.",
                nameof(ConnectionString));
        }

        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
        {
            throw new ArgumentException(
                "T-SQL durable output connection string must specify a database.",
                nameof(ConnectionString));
        }

        builder.ConnectRetryCount = ConnectRetryCount;
        builder.ConnectRetryInterval = checked((int)ConnectRetryInterval.TotalSeconds);

        return new TSqlDurableOutputStoreSettings(
            builder.ConnectionString,
            checked((int)CommandTimeout.TotalSeconds),
            checked((int)SchemaLockTimeout.TotalMilliseconds),
            SchemaManagement);
    }

    internal static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TimeSpan ValidateWholeSeconds(
        TimeSpan value,
        TimeSpan minimum,
        TimeSpan maximum,
        string parameterName)
    {
        if (value < minimum || value > maximum || value.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"T-SQL durable output {parameterName} must be between {minimum} and {maximum} in whole seconds.");
        }

        return value;
    }

    private static TimeSpan ValidateWholeMilliseconds(
        TimeSpan value,
        TimeSpan minimum,
        TimeSpan maximum,
        string parameterName)
    {
        if (value < minimum || value > maximum || value.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"T-SQL durable output {parameterName} must be between {minimum} and {maximum} in whole milliseconds.");
        }

        return value;
    }
}

internal sealed record TSqlDurableOutputStoreSettings(
    string NormalizedConnectionString,
    int CommandTimeoutSeconds,
    int SchemaLockTimeoutMilliseconds,
    TSqlDurableOutputSchemaManagement SchemaManagement);
