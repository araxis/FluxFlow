using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;
using Microsoft.Data.Sqlite;

namespace FluxFlow.Engine.DurableOutput.SqlFile;

public sealed partial class SqlFileDurableOutputStore
{
    public async ValueTask<DurableOutputDeadLetterPage> ListAsync(
        DurableOutputDeadLetterQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureDeliveryInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT d.application_address,
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
                FROM fluxflow_durable_output_deliveries AS d
                INNER JOIN fluxflow_durable_outputs AS o
                  ON o.application_address = d.application_address
                 AND o.message_id = d.message_id
                WHERE d.state = 4
                  AND ($address IS NULL OR d.application_address = $address COLLATE BINARY)
                  AND ($reason IS NULL OR d.dead_letter_reason = $reason)
                  AND ($deadLetteredFrom IS NULL
                       OR d.dead_lettered_at_utc_ticks >= $deadLetteredFrom)
                  AND ($deadLetteredBefore IS NULL
                       OR d.dead_lettered_at_utc_ticks < $deadLetteredBefore)
                  AND ($cursorTime IS NULL
                       OR d.dead_lettered_at_utc_ticks < $cursorTime
                       OR (d.dead_lettered_at_utc_ticks = $cursorTime
                           AND d.application_address COLLATE BINARY > $cursorAddress COLLATE BINARY)
                       OR (d.dead_lettered_at_utc_ticks = $cursorTime
                           AND d.application_address = $cursorAddress COLLATE BINARY
                           AND d.message_id COLLATE BINARY > $cursorMessageId COLLATE BINARY))
                ORDER BY d.dead_lettered_at_utc_ticks DESC,
                         d.application_address COLLATE BINARY,
                         d.message_id COLLATE BINARY
                LIMIT $limit;
                """;
            Add(command, "$address", query.Address?.Value);
            Add(command, "$reason", query.Reason is null ? null : (int)query.Reason.Value);
            Add(command, "$deadLetteredFrom", query.DeadLetteredFrom is null
                ? null
                : ToUtcTicks(query.DeadLetteredFrom.Value));
            Add(command, "$deadLetteredBefore", query.DeadLetteredBefore is null
                ? null
                : ToUtcTicks(query.DeadLetteredBefore.Value));
            Add(command, "$cursorTime", query.Cursor is null
                ? null
                : ToUtcTicks(query.Cursor.DeadLetteredAt));
            Add(command, "$cursorAddress", query.Cursor?.Key.Address.Value);
            Add(command, "$cursorMessageId", query.Cursor?.Key.MessageId.Value);
            Add(command, "$limit", checked(query.PageSize + 1));

            var items = new List<DurableOutputDeadLetterSummary>(query.PageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
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
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("dead-letter list", exception);
        }
    }

    public async ValueTask<DurableOutputDeadLetterDetails?> GetAsync(
        DurableOutputKey key,
        CancellationToken cancellationToken = default)
    {
        ValidateOperationalKey(key);
        await EnsureDeliveryInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT {QualifiedEnvelopeColumns},
                       d.attempt,
                       d.dead_letter_reason,
                       d.dead_lettered_at_utc_ticks,
                       d.dead_lettered_at_offset_minutes,
                       d.dead_letter_generation
                FROM fluxflow_durable_output_deliveries AS d
                INNER JOIN fluxflow_durable_outputs AS o
                  ON o.application_address = d.application_address
                 AND o.message_id = d.message_id
                WHERE d.application_address = $address
                  AND d.message_id = $messageId
                  AND d.state = 4;
                """;
            AddKey(command, key);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;

            var envelope = ReadEnvelope(reader);
            return new DurableOutputDeadLetterDetails(
                envelope,
                ReadPositiveInt32(reader, 19, key, "attempt"),
                ReadDeadLetterReason(reader, 20, key),
                ReadStoredTime(reader, 21, 22, key, "dead-letter timestamp"),
                ReadPositiveInt64(reader, 23, key, "dead-letter generation"));
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("dead-letter lookup", exception);
        }
    }

    public async ValueTask<DurableOutputReplayResult> ReplayAsync(
        DurableOutputReplay replay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replay);
        await EnsureDeliveryInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var transaction = BeginWriteTransaction(connection);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE fluxflow_durable_output_deliveries
                SET state = 1,
                    next_attempt_utc_ticks = $nextAttemptAt,
                    next_attempt_offset_minutes = $nextAttemptAtOffset,
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
                WHERE application_address = $address
                  AND message_id = $messageId
                  AND state = 4
                  AND dead_letter_generation = $expectedGeneration;
                """;
            AddKey(command, replay.Key);
            Add(command, "$nextAttemptAt", ToUtcTicks(replay.NextAttemptAt));
            Add(command, "$nextAttemptAtOffset", ToOffsetMinutes(replay.NextAttemptAt));
            Add(command, "$expectedGeneration", replay.ExpectedGeneration);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

            var status = affected == 1
                ? DurableOutputReplayStatus.Replayed
                : await ResolveReplayStatusAsync(
                        connection,
                        transaction,
                        replay,
                        cancellationToken)
                    .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return new DurableOutputReplayResult(replay.Key, status);
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("dead-letter replay", exception);
        }
    }

    private static async ValueTask<DurableOutputReplayStatus> ResolveReplayStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DurableOutputReplay replay,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT state, dead_letter_generation
            FROM fluxflow_durable_output_deliveries
            WHERE application_address = $address
              AND message_id = $messageId;
            """;
        AddKey(command, replay.Key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return DurableOutputReplayStatus.NotFound;

        var state = reader.GetInt32(0);
        if (state is not (DeliveryPending or DeliveryLeased or DeliveryCompleted or DeliveryDeadLettered))
            throw Corrupt(replay.Key, $"delivery state value {state} is invalid");
        if (state != DeliveryDeadLettered)
            return DurableOutputReplayStatus.NotDeadLettered;

        var generation = ReadPositiveInt64(
            reader,
            1,
            replay.Key,
            "dead-letter generation");
        if (generation != replay.ExpectedGeneration)
            return DurableOutputReplayStatus.GenerationMismatch;

        throw Corrupt(
            replay.Key,
            "replay compare-and-set did not update the matching dead-letter generation");
    }

    private static DurableOutputDeadLetterSummary ReadDeadLetterSummary(SqliteDataReader reader)
    {
        DurableOutputKey? key = null;
        try
        {
            key = new DurableOutputKey(
                ApplicationAddress.Parse(reader.GetString(0)),
                new MessageId(reader.GetString(1)));
            var isError = reader.GetInt32(4);
            if (isError is not (0 or 1))
                throw Corrupt(key.Value, $"is_error value {isError} is invalid");

            return new DurableOutputDeadLetterSummary(
                key.Value,
                reader.GetString(2),
                reader.GetInt32(3),
                isError == 1,
                ReadStoredTime(reader, 5, 6, key.Value, "capture timestamp"),
                ReadPositiveInt32(reader, 7, key.Value, "attempt"),
                ReadDeadLetterReason(reader, 8, key.Value),
                ReadStoredTime(reader, 9, 10, key.Value, "dead-letter timestamp"),
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
                    ? "SQL-file durable output dead-letter row contains an invalid key."
                    : $"SQL-file durable output row '{key}' contains invalid dead-letter metadata.",
                exception);
        }
    }

    private static DurableOutputDeadLetterReason ReadDeadLetterReason(
        SqliteDataReader reader,
        int ordinal,
        DurableOutputKey key)
    {
        try
        {
            if (reader.IsDBNull(ordinal))
                throw Corrupt(key, "dead-letter reason is missing");

            var value = reader.GetInt32(ordinal);
            if (!Enum.IsDefined(typeof(DurableOutputDeadLetterReason), value))
                throw Corrupt(key, $"dead-letter reason value {value} is invalid");
            return (DurableOutputDeadLetterReason)value;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidDataException(
                $"SQL-file durable output row '{key}' contains an invalid dead-letter reason.",
                exception);
        }
    }

    private static int ReadPositiveInt32(
        SqliteDataReader reader,
        int ordinal,
        DurableOutputKey key,
        string field)
    {
        try
        {
            var value = reader.GetInt32(ordinal);
            return value > 0 ? value : throw Corrupt(key, $"{field} must be positive");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidDataException(
                $"SQL-file durable output row '{key}' contains an invalid {field}.",
                exception);
        }
    }

    private static long ReadPositiveInt64(
        SqliteDataReader reader,
        int ordinal,
        DurableOutputKey key,
        string field)
    {
        try
        {
            var value = reader.GetInt64(ordinal);
            return value > 0 ? value : throw Corrupt(key, $"{field} must be positive");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidDataException(
                $"SQL-file durable output row '{key}' contains an invalid {field}.",
                exception);
        }
    }

    private static DateTimeOffset ReadStoredTime(
        SqliteDataReader reader,
        int utcTicksOrdinal,
        int offsetMinutesOrdinal,
        DurableOutputKey key,
        string field)
    {
        try
        {
            if (reader.IsDBNull(utcTicksOrdinal) || reader.IsDBNull(offsetMinutesOrdinal))
                throw Corrupt(key, $"{field} is incomplete");
            return FromStoredTime(
                reader.GetInt64(utcTicksOrdinal),
                reader.GetInt32(offsetMinutesOrdinal),
                key,
                field);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidDataException(
                $"SQL-file durable output row '{key}' contains an invalid {field}.",
                exception);
        }
    }

    private static void ValidateOperationalKey(DurableOutputKey key)
    {
        if (key.Address is null || key.MessageId.IsEmpty)
        {
            throw new ArgumentException(
                "Durable output key must contain an address and message id.",
                nameof(key));
        }
    }
}
