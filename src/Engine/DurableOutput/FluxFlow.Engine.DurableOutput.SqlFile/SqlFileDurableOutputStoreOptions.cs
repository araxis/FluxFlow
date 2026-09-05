namespace FluxFlow.Engine.DurableOutput.SqlFile;

/// <summary>
/// Immutable configuration for <see cref="SqlFileDurableOutputStore"/>.
/// </summary>
public sealed record SqlFileDurableOutputStoreOptions
{
    public static readonly TimeSpan DefaultBusyTimeout = TimeSpan.FromSeconds(30);

    private string? _databasePath;
    private TimeSpan _busyTimeout = DefaultBusyTimeout;

    public string? DatabasePath
    {
        get => _databasePath;
        init => _databasePath = Normalize(value);
    }

    public bool CreateDatabase { get; init; } = true;

    public bool CreateDirectory { get; init; } = true;

    public bool AllowAbsoluteDatabasePath { get; init; }

    public TimeSpan BusyTimeout
    {
        get => _busyTimeout;
        init => _busyTimeout = ValidateBusyTimeout(value, nameof(BusyTimeout));
    }

    internal SqlFileDurableOutputStoreSettings Resolve()
    {
        var databasePath = DatabasePath ?? throw new InvalidOperationException(
            "SQL-file durable output requires a database path.");

        if (Path.IsPathRooted(databasePath) && !AllowAbsoluteDatabasePath)
        {
            throw new InvalidOperationException(
                "SQL-file durable output database path cannot be absolute when absolute paths are disabled.");
        }

        return new SqlFileDurableOutputStoreSettings(
            Path.GetFullPath(databasePath),
            CreateDatabase,
            CreateDirectory,
            BusyTimeout,
            checked((int)Math.Ceiling(BusyTimeout.TotalMilliseconds)));
    }

    internal static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static TimeSpan ValidateBusyTimeout(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"SQL-file durable output busy timeout must be greater than zero and no more than {int.MaxValue} milliseconds.");
        }

        return value;
    }
}

internal sealed record SqlFileDurableOutputStoreSettings(
    string DatabasePath,
    bool CreateDatabase,
    bool CreateDirectory,
    TimeSpan BusyTimeout,
    int BusyTimeoutMilliseconds);
