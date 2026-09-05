using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;
using Microsoft.Data.Sqlite;

namespace FluxFlow.Engine.DurableInput.SqlFile;

public sealed partial class SqlFileDurableInputStore
{
    public async ValueTask<DurableInputDeadLetterPage> ListAsync(
        DurableInputDeadLetterQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT application_address,
                       message_id,
                       contract_name,
                       envelope_schema_version,
                       is_error,
                       enqueued_at_utc_ticks,
                       enqueued_at_offset_minutes,
                       attempt,
                       failure_kind,
                       dead_lettered_at_utc_ticks,
                       dead_letter_generation
                FROM fluxflow_durable_inputs
                WHERE state = $deadLetteredState
                  AND ($address IS NULL OR application_address = $address COLLATE BINARY)
                  AND ($failureKind IS NULL OR failure_kind = $failureKind)
                  AND ($deadLetteredFrom IS NULL OR dead_lettered_at_utc_ticks >= $deadLetteredFrom)
                  AND ($deadLetteredBefore IS NULL OR dead_lettered_at_utc_ticks < $deadLetteredBefore)
                  AND ($cursorTime IS NULL
                       OR dead_lettered_at_utc_ticks < $cursorTime
                       OR (dead_lettered_at_utc_ticks = $cursorTime
                           AND application_address COLLATE BINARY > $cursorAddress COLLATE BINARY)
                       OR (dead_lettered_at_utc_ticks = $cursorTime
                           AND application_address = $cursorAddress COLLATE BINARY
                           AND message_id COLLATE BINARY > $cursorMessageId COLLATE BINARY))
                ORDER BY dead_lettered_at_utc_ticks DESC,
                         application_address COLLATE BINARY,
                         message_id COLLATE BINARY
                LIMIT $limit;
                """;
            Add(command, "$deadLetteredState", (int)DurableInputState.DeadLettered);
            Add(command, "$address", query.Address?.Value);
            Add(command, "$failureKind", query.FailureKind is null ? null : (int)query.FailureKind.Value);
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

            var items = new List<DurableInputDeadLetterSummary>(query.PageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                items.Add(ReadDeadLetterSummary(reader));

            var hasMore = items.Count > query.PageSize;
            if (hasMore)
                items.RemoveAt(items.Count - 1);

            var nextCursor = hasMore
                ? new DurableInputDeadLetterCursor(
                    items[^1].DeadLetteredAt,
                    items[^1].Key)
                : null;
            return new DurableInputDeadLetterPage(items, nextCursor);
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("dead-letter list", exception);
        }
    }

    public async ValueTask<DurableInputDeadLetterDetails?> GetAsync(
        DurableInputKey key,
        CancellationToken cancellationToken = default)
    {
        ValidateOperationalKey(key);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT {EnvelopeColumns},
                       attempt,
                       failure_kind,
                       failure_description,
                       dead_lettered_at_utc_ticks,
                       dead_letter_generation
                FROM fluxflow_durable_inputs
                WHERE application_address = $address
                  AND message_id = $messageId
                  AND state = $deadLetteredState;
                """;
            AddKey(command, key);
            Add(command, "$deadLetteredState", (int)DurableInputState.DeadLettered);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;

            var envelope = ReadEnvelope(reader);
            var attempt = ReadPositiveInt32(reader, EnvelopeColumnCount, key, "attempt");
            var failure = ReadFailure(
                reader,
                EnvelopeColumnCount + 1,
                EnvelopeColumnCount + 2,
                key);
            var deadLetteredAt = ReadUtcTime(
                reader,
                EnvelopeColumnCount + 3,
                key,
                "dead-letter timestamp");
            var generation = ReadPositiveInt64(
                reader,
                EnvelopeColumnCount + 4,
                key,
                "dead-letter generation");
            return new DurableInputDeadLetterDetails(
                envelope,
                attempt,
                failure,
                deadLetteredAt,
                generation);
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("dead-letter lookup", exception);
        }
    }

    public async ValueTask<DurableInputReplayResult> ReplayAsync(
        DurableInputReplay replay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replay);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var transaction = BeginWriteTransaction(connection);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE fluxflow_durable_inputs
                SET state = $pendingState,
                    attempt = 0,
                    next_attempt_utc_ticks = $nextAttemptAt,
                    lease_owner = NULL,
                    lease_token = NULL,
                    leased_at_utc_ticks = NULL,
                    lease_until_utc_ticks = NULL,
                    failure_kind = NULL,
                    failure_description = NULL,
                    delivered_at_utc_ticks = NULL,
                    dead_lettered_at_utc_ticks = NULL
                WHERE application_address = $address
                  AND message_id = $messageId
                  AND state = $deadLetteredState
                  AND dead_letter_generation = $expectedGeneration;
                """;
            AddKey(command, replay.Key);
            Add(command, "$pendingState", (int)DurableInputState.Pending);
            Add(command, "$deadLetteredState", (int)DurableInputState.DeadLettered);
            Add(command, "$nextAttemptAt", ToUtcTicks(replay.NextAttemptAt));
            Add(command, "$expectedGeneration", replay.ExpectedGeneration);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

            var status = affected == 1
                ? DurableInputReplayStatus.Replayed
                : await ResolveReplayStatusAsync(
                        connection,
                        transaction,
                        replay,
                        cancellationToken)
                    .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return new DurableInputReplayResult(replay.Key, status);
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("dead-letter replay", exception);
        }
    }

    private static async ValueTask<DurableInputReplayStatus> ResolveReplayStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DurableInputReplay replay,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT state, dead_letter_generation
            FROM fluxflow_durable_inputs
            WHERE application_address = $address
              AND message_id = $messageId;
            """;
        AddKey(command, replay.Key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return DurableInputReplayStatus.NotFound;

        var stateValue = reader.GetInt32(0);
        if (!Enum.IsDefined(typeof(DurableInputState), stateValue))
            throw Corrupt(replay.Key, $"state value {stateValue} is invalid");
        if ((DurableInputState)stateValue != DurableInputState.DeadLettered)
            return DurableInputReplayStatus.NotDeadLettered;

        var generation = ReadPositiveInt64(
            reader,
            1,
            replay.Key,
            "dead-letter generation");
        if (generation != replay.ExpectedGeneration)
            return DurableInputReplayStatus.GenerationMismatch;

        throw Corrupt(
            replay.Key,
            "replay compare-and-set did not update the matching dead-letter generation");
    }

    private static DurableInputDeadLetterSummary ReadDeadLetterSummary(SqliteDataReader reader)
    {
        DurableInputKey? key = null;
        try
        {
            key = new DurableInputKey(
                ApplicationAddress.Parse(reader.GetString(0)),
                new MessageId(reader.GetString(1)));
            var isErrorValue = reader.GetInt32(4);
            if (isErrorValue is not (0 or 1))
                throw Corrupt(key.Value, $"is_error value {isErrorValue} is invalid");

            return new DurableInputDeadLetterSummary(
                key.Value,
                reader.GetString(2),
                reader.GetInt32(3),
                isErrorValue == 1,
                FromStoredTime(
                    reader.GetInt64(5),
                    reader.GetInt32(6),
                    key.Value,
                    "enqueue timestamp"),
                ReadPositiveInt32(reader, 7, key.Value, "attempt"),
                ReadFailureKind(reader, 8, key.Value),
                ReadUtcTime(reader, 9, key.Value, "dead-letter timestamp"),
                ReadPositiveInt64(reader, 10, key.Value, "dead-letter generation"));
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
                    ? "SQL-file durable input dead-letter row contains an invalid key."
                    : $"SQL-file durable input row '{key}' contains invalid dead-letter metadata.",
                exception);
        }
    }

    private static DurableInputFailureKind ReadFailureKind(
        SqliteDataReader reader,
        int ordinal,
        DurableInputKey key)
    {
        try
        {
            if (reader.IsDBNull(ordinal))
                throw Corrupt(key, "dead-letter failure kind is missing");

            var value = reader.GetInt32(ordinal);
            if (!Enum.IsDefined(typeof(DurableInputFailureKind), value))
                throw Corrupt(key, $"failure kind value {value} is invalid");
            return (DurableInputFailureKind)value;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidDataException(
                $"SQL-file durable input row '{key}' contains an invalid dead-letter failure kind.",
                exception);
        }
    }

    private static DurableInputFailure ReadFailure(
        SqliteDataReader reader,
        int kindOrdinal,
        int descriptionOrdinal,
        DurableInputKey key)
    {
        try
        {
            if (reader.IsDBNull(kindOrdinal) || reader.IsDBNull(descriptionOrdinal))
                throw Corrupt(key, "dead-letter failure fields are incomplete");

            var kindValue = reader.GetInt32(kindOrdinal);
            if (!Enum.IsDefined(typeof(DurableInputFailureKind), kindValue))
                throw Corrupt(key, $"failure kind value {kindValue} is invalid");
            return new DurableInputFailure(
                (DurableInputFailureKind)kindValue,
                reader.GetString(descriptionOrdinal));
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            ArgumentException or FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidDataException(
                $"SQL-file durable input row '{key}' contains invalid dead-letter failure fields.",
                exception);
        }
    }

    private static int ReadPositiveInt32(
        SqliteDataReader reader,
        int ordinal,
        DurableInputKey key,
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
                $"SQL-file durable input row '{key}' contains an invalid {field}.",
                exception);
        }
    }

    private static long ReadPositiveInt64(
        SqliteDataReader reader,
        int ordinal,
        DurableInputKey key,
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
                $"SQL-file durable input row '{key}' contains an invalid {field}.",
                exception);
        }
    }

    private static DateTimeOffset ReadUtcTime(
        SqliteDataReader reader,
        int ordinal,
        DurableInputKey key,
        string field)
    {
        try
        {
            return new DateTimeOffset(reader.GetInt64(ordinal), TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is
            ArgumentOutOfRangeException or FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidDataException(
                $"SQL-file durable input row '{key}' contains an invalid {field}.",
                exception);
        }
    }

    private static void ValidateOperationalKey(DurableInputKey key)
    {
        if (key.Address is null || key.MessageId.IsEmpty)
            throw new ArgumentException("Durable input key must contain an address and message id.", nameof(key));
    }
}
