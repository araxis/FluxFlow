using System.Data;
using Microsoft.Data.SqlClient;

namespace FluxFlow.Engine.DurableInput.TSql;

public sealed partial class TSqlDurableInputStore
{
    public async ValueTask<DurableInputStatusSnapshot> GetStatusAsync(
        DurableInputStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = CreateCommand(connection);
        command.CommandText = """
            SELECT COALESCE(SUM(CASE WHEN state = @pendingState THEN CAST(1 AS bigint) ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN state = @pendingState
                                         AND next_attempt_utc_ticks <= @observedAt
                                    THEN CAST(1 AS bigint) ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN state = @leasedState THEN CAST(1 AS bigint) ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN state = @leasedState
                                         AND lease_until_utc_ticks <= @observedAt
                                    THEN CAST(1 AS bigint) ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN state = @deliveredState THEN CAST(1 AS bigint) ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN state = @deadLetteredState THEN CAST(1 AS bigint) ELSE 0 END), 0),
                   MIN(CASE
                           WHEN state = @pendingState
                                AND next_attempt_utc_ticks <= @observedAt
                               THEN next_attempt_utc_ticks
                           WHEN state = @leasedState
                                AND lease_until_utc_ticks <= @observedAt
                               THEN lease_until_utc_ticks
                       END),
                   MIN(CASE WHEN state = @leasedState
                                  AND lease_until_utc_ticks > @observedAt
                            THEN lease_until_utc_ticks END),
                   COALESCE(SUM(CASE WHEN state NOT IN (
                       @pendingState,
                       @leasedState,
                       @deliveredState,
                       @deadLetteredState)
                       THEN CAST(1 AS bigint) ELSE 0 END), 0)
            FROM dbo.fluxflow_relational_inputs;
            """;
        RelationalDurableInputRows.Add(
            command,
            "@pendingState",
            SqlDbType.TinyInt,
            (byte)DurableInputState.Pending);
        RelationalDurableInputRows.Add(
            command,
            "@leasedState",
            SqlDbType.TinyInt,
            (byte)DurableInputState.Leased);
        RelationalDurableInputRows.Add(
            command,
            "@deliveredState",
            SqlDbType.TinyInt,
            (byte)DurableInputState.Delivered);
        RelationalDurableInputRows.Add(
            command,
            "@deadLetteredState",
            SqlDbType.TinyInt,
            (byte)DurableInputState.DeadLettered);
        RelationalDurableInputRows.Add(
            command,
            "@observedAt",
            SqlDbType.BigInt,
            query.ObservedAt.UtcTicks);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("T-SQL durable input status query returned no row.");
        if (reader.GetInt64(8) != 0)
            throw new InvalidDataException("T-SQL durable input status found an invalid state value.");

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

    private static DateTimeOffset? ReadStatusTime(SqlDataReader reader, int ordinal)
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
                "T-SQL durable input status contains an invalid timestamp.",
                exception);
        }
    }
}
