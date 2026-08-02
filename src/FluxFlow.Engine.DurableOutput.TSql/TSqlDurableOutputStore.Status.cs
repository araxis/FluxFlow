using System.Data;
using Microsoft.Data.SqlClient;

namespace FluxFlow.Engine.DurableOutput.TSql;

public sealed partial class TSqlDurableOutputStore
{
    public async ValueTask<DurableOutputStatusSnapshot> GetStatusAsync(
        DurableOutputStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = CreateCommand(connection);
        command.CommandText = """
            WITH output_status AS (
                SELECT o.captured_at_utc_ticks,
                       d.state,
                       d.next_attempt_utc_ticks,
                       d.lease_until_utc_ticks
                FROM dbo.fluxflow_relational_outputs AS o
                LEFT JOIN dbo.fluxflow_relational_output_deliveries AS d
                  ON d.application_address = o.application_address
                 AND d.message_id = o.message_id
            )
            SELECT COUNT_BIG(*),
                   COALESCE(SUM(CASE WHEN state IS NULL THEN CAST(1 AS bigint) ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN state IS NULL
                                          AND captured_at_utc_ticks <= @observedAt
                                     THEN CAST(1 AS bigint) ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN state = @pendingState THEN CAST(1 AS bigint) ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN state = @pendingState
                                          AND next_attempt_utc_ticks <= @observedAt
                                     THEN CAST(1 AS bigint) ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN state = @leasedState THEN CAST(1 AS bigint) ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN state = @leasedState
                                          AND lease_until_utc_ticks <= @observedAt
                                     THEN CAST(1 AS bigint) ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN state = @completedState THEN CAST(1 AS bigint) ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN state = @deadLetteredState THEN CAST(1 AS bigint) ELSE 0 END), 0),
                   MIN(CASE
                           WHEN state IS NULL AND captured_at_utc_ticks <= @observedAt
                               THEN captured_at_utc_ticks
                           WHEN state = @pendingState AND next_attempt_utc_ticks <= @observedAt
                               THEN next_attempt_utc_ticks
                           WHEN state = @leasedState AND lease_until_utc_ticks <= @observedAt
                               THEN lease_until_utc_ticks
                       END),
                   MIN(CASE WHEN state = @leasedState
                                  AND lease_until_utc_ticks > @observedAt
                            THEN lease_until_utc_ticks END),
                   COALESCE(SUM(CASE WHEN state IS NOT NULL AND state NOT IN (
                       @pendingState,
                       @leasedState,
                       @completedState,
                       @deadLetteredState)
                       THEN CAST(1 AS bigint) ELSE 0 END), 0),
                   (SELECT COUNT_BIG(*)
                    FROM dbo.fluxflow_relational_output_deliveries AS d
                    LEFT JOIN dbo.fluxflow_relational_outputs AS o
                      ON o.application_address = d.application_address
                     AND o.message_id = d.message_id
                    WHERE o.application_address IS NULL)
            FROM output_status;
            """;
        RelationalDurableOutputRows.Add(command, "@pendingState", SqlDbType.TinyInt, DeliveryPending);
        RelationalDurableOutputRows.Add(command, "@leasedState", SqlDbType.TinyInt, DeliveryLeased);
        RelationalDurableOutputRows.Add(command, "@completedState", SqlDbType.TinyInt, DeliveryCompleted);
        RelationalDurableOutputRows.Add(command, "@deadLetteredState", SqlDbType.TinyInt, DeliveryDeadLettered);
        RelationalDurableOutputRows.Add(
            command,
            "@observedAt",
            SqlDbType.BigInt,
            query.ObservedAt.UtcTicks);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("T-SQL durable output status query returned no row.");
        if (reader.GetInt64(11) != 0)
            throw new InvalidDataException("T-SQL durable output status found an invalid delivery state value.");
        if (reader.GetInt64(12) != 0)
            throw new InvalidDataException("T-SQL durable output status found an orphan delivery row.");

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

    private static DateTimeOffset? ReadOutputStatusTime(SqlDataReader reader, int ordinal)
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
                "T-SQL durable output status contains an invalid timestamp.",
                exception);
        }
    }
}
