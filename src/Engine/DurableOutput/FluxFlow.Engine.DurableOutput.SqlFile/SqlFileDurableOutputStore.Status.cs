using Microsoft.Data.Sqlite;

namespace FluxFlow.Engine.DurableOutput.SqlFile;

public sealed partial class SqlFileDurableOutputStore
{
    public async ValueTask<DurableOutputStatusSnapshot> GetStatusAsync(
        DurableOutputStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var connection = await OpenStatusConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            var hasDeliveryTable = await HasDeliveryTableAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = hasDeliveryTable
                ? StatusWithDeliveryCommandText
                : CaptureOnlyStatusCommandText;
            Add(command, "$pendingState", DeliveryPending);
            Add(command, "$leasedState", DeliveryLeased);
            Add(command, "$completedState", DeliveryCompleted);
            Add(command, "$deadLetteredState", DeliveryDeadLettered);
            Add(command, "$observedAt", query.ObservedAt.UtcTicks);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("SQL-file durable output status query returned no row.");
            if (reader.GetInt64(11) != 0)
                throw new InvalidDataException("SQL-file durable output status found an invalid delivery state value.");
            if (reader.GetInt64(12) != 0)
                throw new InvalidDataException("SQL-file durable output status found an orphan delivery row.");

            return new DurableOutputStatusSnapshot(
                query.ObservedAt,
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                ReadOutputStatusTime(reader, 9),
                ReadOutputStatusTime(reader, 10));
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("status inspection", exception);
        }
    }

    private static async ValueTask<bool> HasDeliveryTableAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = 'fluxflow_durable_output_deliveries';
            """;
        var count = Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
        return count == 1;
    }

    private async ValueTask<SqliteConnection> OpenStatusConnectionAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!File.Exists(_settings.DatabasePath))
        {
            throw new FileNotFoundException(
                $"SQL-file durable output database '{_settings.DatabasePath}' does not exist.",
                _settings.DatabasePath);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _settings.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = Math.Max(1, (int)Math.Ceiling(_settings.BusyTimeout.TotalSeconds))
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = FormattableString.Invariant(
                $"PRAGMA busy_timeout = {_settings.BusyTimeoutMilliseconds}; PRAGMA foreign_keys = ON;");
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static DateTimeOffset? ReadOutputStatusTime(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;

        try
        {
            return new DateTimeOffset(reader.GetInt64(ordinal), TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException(
                "SQL-file durable output status contains an invalid timestamp.",
                exception);
        }
    }

    private const string CaptureOnlyStatusCommandText = """
        SELECT COUNT(*),
               COUNT(*),
               COALESCE(SUM(CASE WHEN captured_at_utc_ticks <= $observedAt THEN 1 ELSE 0 END), 0),
               0,
               0,
               0,
               0,
               0,
               0,
               MIN(CASE WHEN captured_at_utc_ticks <= $observedAt
                        THEN captured_at_utc_ticks END),
               NULL,
               0,
               0
        FROM fluxflow_durable_outputs;
        """;

    private const string StatusWithDeliveryCommandText = """
        WITH output_status AS (
            SELECT o.captured_at_utc_ticks,
                   d.state,
                   d.next_attempt_utc_ticks,
                   d.lease_until_utc_ticks
            FROM fluxflow_durable_outputs AS o
            LEFT JOIN fluxflow_durable_output_deliveries AS d
              ON d.application_address = o.application_address
             AND d.message_id = o.message_id
        )
        SELECT COUNT(*),
               COALESCE(SUM(CASE WHEN state IS NULL THEN 1 ELSE 0 END), 0),
               COALESCE(SUM(CASE WHEN state IS NULL
                                      AND captured_at_utc_ticks <= $observedAt
                                 THEN 1 ELSE 0 END), 0),
               COALESCE(SUM(CASE WHEN state = $pendingState THEN 1 ELSE 0 END), 0),
               COALESCE(SUM(CASE WHEN state = $pendingState
                                      AND next_attempt_utc_ticks <= $observedAt
                                 THEN 1 ELSE 0 END), 0),
               COALESCE(SUM(CASE WHEN state = $leasedState THEN 1 ELSE 0 END), 0),
               COALESCE(SUM(CASE WHEN state = $leasedState
                                      AND lease_until_utc_ticks <= $observedAt
                                 THEN 1 ELSE 0 END), 0),
               COALESCE(SUM(CASE WHEN state = $completedState THEN 1 ELSE 0 END), 0),
               COALESCE(SUM(CASE WHEN state = $deadLetteredState THEN 1 ELSE 0 END), 0),
               MIN(CASE
                       WHEN state IS NULL AND captured_at_utc_ticks <= $observedAt
                           THEN captured_at_utc_ticks
                       WHEN state = $pendingState AND next_attempt_utc_ticks <= $observedAt
                           THEN next_attempt_utc_ticks
                       WHEN state = $leasedState AND lease_until_utc_ticks <= $observedAt
                           THEN lease_until_utc_ticks
                   END),
               MIN(CASE WHEN state = $leasedState
                              AND lease_until_utc_ticks > $observedAt
                        THEN lease_until_utc_ticks END),
               COALESCE(SUM(CASE WHEN state IS NOT NULL AND state NOT IN (
                   $pendingState,
                   $leasedState,
                   $completedState,
                   $deadLetteredState)
                   THEN 1 ELSE 0 END), 0),
               (SELECT COUNT(*)
                FROM fluxflow_durable_output_deliveries AS d
                LEFT JOIN fluxflow_durable_outputs AS o
                  ON o.application_address = d.application_address
                 AND o.message_id = d.message_id
                WHERE o.application_address IS NULL)
        FROM output_status;
        """;
}
