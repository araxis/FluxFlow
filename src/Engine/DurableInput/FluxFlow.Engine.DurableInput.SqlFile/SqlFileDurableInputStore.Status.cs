using Microsoft.Data.Sqlite;

namespace FluxFlow.Engine.DurableInput.SqlFile;

public sealed partial class SqlFileDurableInputStore
{
    public async ValueTask<DurableInputStatusSnapshot> GetStatusAsync(
        DurableInputStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var connection = await OpenStatusConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COALESCE(SUM(CASE WHEN state = $pendingState THEN 1 ELSE 0 END), 0),
                       COALESCE(SUM(CASE WHEN state = $pendingState
                                             AND next_attempt_utc_ticks <= $observedAt
                                        THEN 1 ELSE 0 END), 0),
                       COALESCE(SUM(CASE WHEN state = $leasedState THEN 1 ELSE 0 END), 0),
                       COALESCE(SUM(CASE WHEN state = $leasedState
                                             AND lease_until_utc_ticks <= $observedAt
                                        THEN 1 ELSE 0 END), 0),
                       COALESCE(SUM(CASE WHEN state = $deliveredState THEN 1 ELSE 0 END), 0),
                       COALESCE(SUM(CASE WHEN state = $deadLetteredState THEN 1 ELSE 0 END), 0),
                       MIN(CASE
                               WHEN state = $pendingState
                                    AND next_attempt_utc_ticks <= $observedAt
                                   THEN next_attempt_utc_ticks
                               WHEN state = $leasedState
                                    AND lease_until_utc_ticks <= $observedAt
                                   THEN lease_until_utc_ticks
                           END),
                       MIN(CASE WHEN state = $leasedState
                                      AND lease_until_utc_ticks > $observedAt
                                THEN lease_until_utc_ticks END),
                       COALESCE(SUM(CASE WHEN state NOT IN (
                           $pendingState,
                           $leasedState,
                           $deliveredState,
                           $deadLetteredState)
                           THEN 1 ELSE 0 END), 0)
                FROM fluxflow_durable_inputs;
                """;
            Add(command, "$pendingState", (int)DurableInputState.Pending);
            Add(command, "$leasedState", (int)DurableInputState.Leased);
            Add(command, "$deliveredState", (int)DurableInputState.Delivered);
            Add(command, "$deadLetteredState", (int)DurableInputState.DeadLettered);
            Add(command, "$observedAt", query.ObservedAt.UtcTicks);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("SQL-file durable input status query returned no row.");
            if (reader.GetInt64(8) != 0)
                throw new InvalidDataException("SQL-file durable input status found an invalid state value.");

            return new DurableInputStatusSnapshot(
                query.ObservedAt,
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                ReadStatusTime(reader, 6),
                ReadStatusTime(reader, 7));
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("status inspection", exception);
        }
    }

    private async ValueTask<SqliteConnection> OpenStatusConnectionAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!File.Exists(_settings.DatabasePath))
        {
            throw new FileNotFoundException(
                $"SQL-file durable input database '{_settings.DatabasePath}' does not exist.",
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
                $"PRAGMA busy_timeout = {_settings.BusyTimeoutMilliseconds};");
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static DateTimeOffset? ReadStatusTime(SqliteDataReader reader, int ordinal)
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
                "SQL-file durable input status contains an invalid timestamp.",
                exception);
        }
    }
}
