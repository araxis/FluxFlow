using System.Data;
using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;
using Microsoft.Data.SqlClient;

namespace FluxFlow.Engine.DurableOutput.TSql;

public sealed partial class TSqlDurableOutputStore
{
    public async ValueTask<DurableOutputDeadLetterPage> ListAsync(
        DurableOutputDeadLetterQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (query.Address is { } address)
            RelationalDurableOutputRows.ValidateAddress(address.Value, nameof(query));
        if (query.Cursor is { } cursor)
            ValidateKey(cursor.Key);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(connection);
        command.CommandText = """
            SELECT TOP (@limit)
                   d.application_address,
                   d.message_id,
                   o.contract_name,
                   o.envelope_schema_version,
                   o.is_error,
                   o.captured_at_utc_ticks,
                   o.captured_at_offset_minutes,
                   d.attempt,
                   d.dead_letter_reason,
                   d.dead_lettered_at_utc_ticks,
                   d.dead_lettered_at_offset_minutes,
                   d.dead_letter_generation
            FROM dbo.fluxflow_relational_output_deliveries AS d
            INNER JOIN dbo.fluxflow_relational_outputs AS o
              ON o.application_address = d.application_address
             AND o.message_id = d.message_id
            WHERE d.state = 4
              AND (@address IS NULL
                   OR d.application_address = @address COLLATE Latin1_General_100_BIN2)
              AND (@reason IS NULL OR d.dead_letter_reason = @reason)
              AND (@deadLetteredFrom IS NULL
                   OR d.dead_lettered_at_utc_ticks >= @deadLetteredFrom)
              AND (@deadLetteredBefore IS NULL
                   OR d.dead_lettered_at_utc_ticks < @deadLetteredBefore)
              AND (@cursorTime IS NULL
                   OR d.dead_lettered_at_utc_ticks < @cursorTime
                   OR (d.dead_lettered_at_utc_ticks = @cursorTime
                       AND d.application_address COLLATE Latin1_General_100_BIN2
                           > @cursorAddress COLLATE Latin1_General_100_BIN2)
                   OR (d.dead_lettered_at_utc_ticks = @cursorTime
                       AND d.application_address COLLATE Latin1_General_100_BIN2
                           = @cursorAddress COLLATE Latin1_General_100_BIN2
                       AND d.message_id COLLATE Latin1_General_100_BIN2
                           > @cursorMessageId COLLATE Latin1_General_100_BIN2))
            ORDER BY d.dead_lettered_at_utc_ticks DESC,
                     d.application_address COLLATE Latin1_General_100_BIN2,
                     d.message_id COLLATE Latin1_General_100_BIN2;
            """;
        RelationalDurableOutputRows.Add(command, "@limit", SqlDbType.Int, checked(query.PageSize + 1));
        RelationalDurableOutputRows.AddNVarChar(command, "@address", query.Address?.Value, 300);
        RelationalDurableOutputRows.Add(command, "@reason", SqlDbType.Int, query.Reason is null ? null : (int)query.Reason.Value);
        RelationalDurableOutputRows.Add(command, "@deadLetteredFrom", SqlDbType.BigInt, query.DeadLetteredFrom?.UtcTicks);
        RelationalDurableOutputRows.Add(command, "@deadLetteredBefore", SqlDbType.BigInt, query.DeadLetteredBefore?.UtcTicks);
        RelationalDurableOutputRows.Add(command, "@cursorTime", SqlDbType.BigInt, query.Cursor?.DeadLetteredAt.UtcTicks);
        RelationalDurableOutputRows.AddNVarChar(command, "@cursorAddress", query.Cursor?.Key.Address.Value, 300);
        RelationalDurableOutputRows.AddNVarChar(command, "@cursorMessageId", query.Cursor?.Key.MessageId.Value, 128);

        var items = new List<DurableOutputDeadLetterSummary>(query.PageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            items.Add(ReadDeadLetterSummary(reader));

        var hasMore = items.Count > query.PageSize;
        if (hasMore)
            items.RemoveAt(items.Count - 1);
        var nextCursor = hasMore
            ? new DurableOutputDeadLetterCursor(items[^1].DeadLetteredAt, items[^1].Key)
            : null;
        return new DurableOutputDeadLetterPage(items, nextCursor);
    }

    public async ValueTask<DurableOutputDeadLetterDetails?> GetAsync(
        DurableOutputKey key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT {RelationalDurableOutputRows.EnvelopeColumns},
                   d.attempt,
                   d.dead_letter_reason,
                   d.dead_lettered_at_utc_ticks,
                   d.dead_lettered_at_offset_minutes,
                   d.dead_letter_generation
            FROM dbo.fluxflow_relational_output_deliveries AS d
            INNER JOIN dbo.fluxflow_relational_outputs AS o
              ON o.application_address = d.application_address
             AND o.message_id = d.message_id
            WHERE d.application_address = @address COLLATE Latin1_General_100_BIN2
              AND d.message_id = @messageId COLLATE Latin1_General_100_BIN2
              AND d.state = 4;
            """;
        RelationalDurableOutputRows.AddKey(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return new DurableOutputDeadLetterDetails(
            RelationalDurableOutputRows.ReadEnvelope(reader),
            ReadPositiveInt32(reader, 19, key, "attempt"),
            ReadDeadLetterReason(reader, 20, key),
            RelationalDurableOutputRows.ReadStoredTime(reader, 21, 22, key, "dead-letter timestamp"),
            ReadPositiveInt64(reader, 23, key, "dead-letter generation"));
    }

    public async ValueTask<DurableOutputReplayResult> ReplayAsync(
        DurableOutputReplay replay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replay);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(replay.Key);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken)
            .ConfigureAwait(false);
        await using var command = CreateCommand(connection, transaction);
        command.CommandText = """
            UPDATE dbo.fluxflow_relational_output_deliveries
            SET state = 1,
                next_attempt_utc_ticks = @nextAttemptAt,
                next_attempt_offset_minutes = @nextAttemptAtOffset,
                lease_token = NULL,
                lease_owner = NULL,
                leased_at_utc_ticks = NULL,
                leased_at_offset_minutes = NULL,
                lease_until_utc_ticks = NULL,
                lease_until_offset_minutes = NULL,
                attempt = 0,
                delivered_at_utc_ticks = NULL,
                delivered_at_offset_minutes = NULL,
                dead_letter_reason = NULL,
                dead_lettered_at_utc_ticks = NULL,
                dead_lettered_at_offset_minutes = NULL
            WHERE application_address = @address COLLATE Latin1_General_100_BIN2
              AND message_id = @messageId COLLATE Latin1_General_100_BIN2
              AND state = 4
              AND dead_letter_generation = @expectedGeneration;
            """;
        RelationalDurableOutputRows.AddKey(command, replay.Key);
        RelationalDurableOutputRows.Add(command, "@nextAttemptAt", SqlDbType.BigInt, replay.NextAttemptAt.UtcTicks);
        RelationalDurableOutputRows.Add(
            command,
            "@nextAttemptAtOffset",
            SqlDbType.SmallInt,
            RelationalDurableOutputRows.ToOffsetMinutes(replay.NextAttemptAt));
        RelationalDurableOutputRows.Add(command, "@expectedGeneration", SqlDbType.BigInt, replay.ExpectedGeneration);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var status = affected == 1
            ? DurableOutputReplayStatus.Replayed
            : await ResolveReplayStatusAsync(connection, transaction, replay, cancellationToken)
                .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return new DurableOutputReplayResult(replay.Key, status);
    }

    private async ValueTask<DurableOutputReplayStatus> ResolveReplayStatusAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DurableOutputReplay replay,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction);
        command.CommandText = """
            SELECT state, dead_letter_generation
            FROM dbo.fluxflow_relational_output_deliveries WITH (UPDLOCK, HOLDLOCK)
            WHERE application_address = @address COLLATE Latin1_General_100_BIN2
              AND message_id = @messageId COLLATE Latin1_General_100_BIN2;
            """;
        RelationalDurableOutputRows.AddKey(command, replay.Key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return DurableOutputReplayStatus.NotFound;
        var state = reader.GetByte(0);
        if (state is not (DeliveryPending or DeliveryLeased or DeliveryCompleted or DeliveryDeadLettered))
            throw RelationalDurableOutputRows.Corrupt(replay.Key, $"delivery state value {state} is invalid");
        if (state != DeliveryDeadLettered)
            return DurableOutputReplayStatus.NotDeadLettered;
        var generation = ReadPositiveInt64(reader, 1, replay.Key, "dead-letter generation");
        if (generation != replay.ExpectedGeneration)
            return DurableOutputReplayStatus.GenerationMismatch;
        throw RelationalDurableOutputRows.Corrupt(
            replay.Key,
            "matching replay compare-and-set did not update");
    }

    private static DurableOutputDeadLetterSummary ReadDeadLetterSummary(SqlDataReader reader)
    {
        DurableOutputKey? key = null;
        try
        {
            key = new DurableOutputKey(
                ApplicationAddress.Parse(reader.GetString(0)),
                new MessageId(reader.GetString(1)));
            return new DurableOutputDeadLetterSummary(
                key.Value,
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetBoolean(4),
                RelationalDurableOutputRows.ReadStoredTime(reader, 5, 6, key.Value, "capture timestamp"),
                ReadPositiveInt32(reader, 7, key.Value, "attempt"),
                ReadDeadLetterReason(reader, 8, key.Value),
                RelationalDurableOutputRows.ReadStoredTime(reader, 9, 10, key.Value, "dead-letter timestamp"),
                ReadPositiveInt64(reader, 11, key.Value, "dead-letter generation"));
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            ArgumentException or FormatException or InvalidCastException or
            InvalidOperationException or OverflowException)
        {
            throw new InvalidDataException(
                key is null
                    ? "Relational durable-output dead-letter row contains an invalid key."
                    : $"Relational durable-output row '{key}' contains invalid dead-letter metadata.",
                exception);
        }
    }

    private static DurableOutputDeadLetterReason ReadDeadLetterReason(
        SqlDataReader reader,
        int ordinal,
        DurableOutputKey key)
    {
        if (reader.IsDBNull(ordinal))
            throw RelationalDurableOutputRows.Corrupt(key, "dead-letter reason is missing");
        var value = reader.GetInt32(ordinal);
        if (!Enum.IsDefined(typeof(DurableOutputDeadLetterReason), value))
            throw RelationalDurableOutputRows.Corrupt(key, $"dead-letter reason value {value} is invalid");
        return (DurableOutputDeadLetterReason)value;
    }

    private static int ReadPositiveInt32(
        SqlDataReader reader,
        int ordinal,
        DurableOutputKey key,
        string field)
    {
        var value = reader.GetInt32(ordinal);
        return value > 0
            ? value
            : throw RelationalDurableOutputRows.Corrupt(key, $"{field} must be positive");
    }

    private static long ReadPositiveInt64(
        SqlDataReader reader,
        int ordinal,
        DurableOutputKey key,
        string field)
    {
        var value = reader.GetInt64(ordinal);
        return value > 0
            ? value
            : throw RelationalDurableOutputRows.Corrupt(key, $"{field} must be positive");
    }
}
