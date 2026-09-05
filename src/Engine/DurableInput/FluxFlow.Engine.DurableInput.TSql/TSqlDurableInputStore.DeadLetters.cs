using System.Data;
using FluxFlow.Composition.Addressing;
using Microsoft.Data.SqlClient;

namespace FluxFlow.Engine.DurableInput.TSql;

public sealed partial class TSqlDurableInputStore
{
    public async ValueTask<DurableInputDeadLetterPage> ListAsync(
        DurableInputDeadLetterQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (query.Address is not null)
            RelationalDurableInputRows.ValidateAddress(query.Address.Value, nameof(query));
        if (query.Cursor is not null)
            RelationalDurableInputRows.ValidateKey(query.Cursor.Key);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = CreateCommand(connection);
        command.CommandText = """
            SELECT TOP (@limit)
                   application_address,
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
            FROM dbo.fluxflow_relational_inputs
            WHERE state = @deadLetteredState
              AND (@address IS NULL
                   OR application_address = @address COLLATE Latin1_General_100_BIN2)
              AND (@failureKind IS NULL OR failure_kind = @failureKind)
              AND (@deadLetteredFrom IS NULL
                   OR dead_lettered_at_utc_ticks >= @deadLetteredFrom)
              AND (@deadLetteredBefore IS NULL
                   OR dead_lettered_at_utc_ticks < @deadLetteredBefore)
              AND (@cursorTime IS NULL
                   OR dead_lettered_at_utc_ticks < @cursorTime
                   OR (dead_lettered_at_utc_ticks = @cursorTime
                       AND application_address COLLATE Latin1_General_100_BIN2
                           > @cursorAddress COLLATE Latin1_General_100_BIN2)
                   OR (dead_lettered_at_utc_ticks = @cursorTime
                       AND application_address = @cursorAddress COLLATE Latin1_General_100_BIN2
                       AND message_id COLLATE Latin1_General_100_BIN2
                           > @cursorMessageId COLLATE Latin1_General_100_BIN2))
            ORDER BY dead_lettered_at_utc_ticks DESC,
                     application_address COLLATE Latin1_General_100_BIN2,
                     message_id COLLATE Latin1_General_100_BIN2;
            """;
        RelationalDurableInputRows.Add(
            command,
            "@limit",
            SqlDbType.Int,
            checked(query.PageSize + 1));
        RelationalDurableInputRows.Add(
            command,
            "@deadLetteredState",
            SqlDbType.TinyInt,
            (byte)DurableInputState.DeadLettered);
        RelationalDurableInputRows.AddNVarChar(
            command,
            "@address",
            query.Address?.Value,
            RelationalDurableInputRows.AddressMaxLength);
        RelationalDurableInputRows.Add(
            command,
            "@failureKind",
            SqlDbType.Int,
            query.FailureKind is null ? null : (int)query.FailureKind.Value);
        RelationalDurableInputRows.Add(
            command,
            "@deadLetteredFrom",
            SqlDbType.BigInt,
            query.DeadLetteredFrom?.UtcTicks);
        RelationalDurableInputRows.Add(
            command,
            "@deadLetteredBefore",
            SqlDbType.BigInt,
            query.DeadLetteredBefore?.UtcTicks);
        RelationalDurableInputRows.Add(
            command,
            "@cursorTime",
            SqlDbType.BigInt,
            query.Cursor?.DeadLetteredAt.UtcTicks);
        RelationalDurableInputRows.AddNVarChar(
            command,
            "@cursorAddress",
            query.Cursor?.Key.Address.Value,
            RelationalDurableInputRows.AddressMaxLength);
        RelationalDurableInputRows.AddNVarChar(
            command,
            "@cursorMessageId",
            query.Cursor?.Key.MessageId.Value,
            RelationalDurableInputRows.MessageIdMaxLength);

        var items = new List<DurableInputDeadLetterSummary>(query.PageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            items.Add(ReadDeadLetterSummary(reader));

        var hasMore = items.Count > query.PageSize;
        if (hasMore)
            items.RemoveAt(items.Count - 1);

        var nextCursor = hasMore
            ? new DurableInputDeadLetterCursor(items[^1].DeadLetteredAt, items[^1].Key)
            : null;
        return new DurableInputDeadLetterPage(items, nextCursor);
    }

    public async ValueTask<DurableInputDeadLetterDetails?> GetAsync(
        DurableInputKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RelationalDurableInputRows.ValidateKey(key);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT {RelationalDurableInputRows.EnvelopeColumns},
                   i.attempt,
                   i.failure_kind,
                   i.failure_description,
                   i.dead_lettered_at_utc_ticks,
                   i.dead_letter_generation
            FROM dbo.fluxflow_relational_inputs AS i
            WHERE i.application_address = @address COLLATE Latin1_General_100_BIN2
              AND i.message_id = @messageId COLLATE Latin1_General_100_BIN2
              AND i.state = @deadLetteredState;
            """;
        RelationalDurableInputRows.AddKey(command, key);
        RelationalDurableInputRows.Add(
            command,
            "@deadLetteredState",
            SqlDbType.TinyInt,
            (byte)DurableInputState.DeadLettered);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var envelope = RelationalDurableInputRows.ReadEnvelope(reader);
        var attempt = RelationalDurableInputRows.ReadPositiveInt32(
            reader,
            RelationalDurableInputRows.EnvelopeColumnCount,
            key,
            "attempt");
        var failure = RelationalDurableInputRows.ReadFailure(
            reader,
            RelationalDurableInputRows.EnvelopeColumnCount + 1,
            RelationalDurableInputRows.EnvelopeColumnCount + 2,
            key);
        var deadLetteredAt = RelationalDurableInputRows.ReadUtcTime(
            reader,
            RelationalDurableInputRows.EnvelopeColumnCount + 3,
            key,
            "dead-letter timestamp");
        var generation = RelationalDurableInputRows.ReadPositiveInt64(
            reader,
            RelationalDurableInputRows.EnvelopeColumnCount + 4,
            key,
            "dead-letter generation");
        return new DurableInputDeadLetterDetails(
            envelope,
            attempt,
            failure,
            deadLetteredAt,
            generation);
    }

    public async ValueTask<DurableInputReplayResult> ReplayAsync(
        DurableInputReplay replay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replay);
        cancellationToken.ThrowIfCancellationRequested();
        RelationalDurableInputRows.ValidateKey(replay.Key);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            .ConfigureAwait(false);
        await using var command = CreateCommand(connection, transaction);
        command.CommandText = """
            UPDATE dbo.fluxflow_relational_inputs
            SET state = @pendingState,
                attempt = 0,
                next_attempt_utc_ticks = @nextAttemptAt,
                lease_owner = NULL,
                lease_token = NULL,
                leased_at_utc_ticks = NULL,
                lease_until_utc_ticks = NULL,
                failure_kind = NULL,
                failure_description = NULL,
                delivered_at_utc_ticks = NULL,
                dead_lettered_at_utc_ticks = NULL
            WHERE application_address = @address COLLATE Latin1_General_100_BIN2
              AND message_id = @messageId COLLATE Latin1_General_100_BIN2
              AND state = @deadLetteredState
              AND dead_letter_generation = @expectedGeneration;
            """;
        RelationalDurableInputRows.AddKey(command, replay.Key);
        RelationalDurableInputRows.Add(
            command,
            "@pendingState",
            SqlDbType.TinyInt,
            (byte)DurableInputState.Pending);
        RelationalDurableInputRows.Add(
            command,
            "@deadLetteredState",
            SqlDbType.TinyInt,
            (byte)DurableInputState.DeadLettered);
        RelationalDurableInputRows.Add(
            command,
            "@nextAttemptAt",
            SqlDbType.BigInt,
            replay.NextAttemptAt.UtcTicks);
        RelationalDurableInputRows.Add(
            command,
            "@expectedGeneration",
            SqlDbType.BigInt,
            replay.ExpectedGeneration);
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

    private async ValueTask<DurableInputReplayStatus> ResolveReplayStatusAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DurableInputReplay replay,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction);
        command.CommandText = """
            SELECT state, dead_letter_generation
            FROM dbo.fluxflow_relational_inputs WITH (UPDLOCK, HOLDLOCK)
            WHERE application_address = @address COLLATE Latin1_General_100_BIN2
              AND message_id = @messageId COLLATE Latin1_General_100_BIN2;
            """;
        RelationalDurableInputRows.AddKey(command, replay.Key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return DurableInputReplayStatus.NotFound;

        var stateValue = reader.GetByte(0);
        if (!Enum.IsDefined(typeof(DurableInputState), (int)stateValue))
            throw RelationalDurableInputRows.Corrupt(replay.Key, $"state value {stateValue} is invalid");
        if ((DurableInputState)stateValue != DurableInputState.DeadLettered)
            return DurableInputReplayStatus.NotDeadLettered;

        var generation = RelationalDurableInputRows.ReadPositiveInt64(
            reader,
            1,
            replay.Key,
            "dead-letter generation");
        if (generation != replay.ExpectedGeneration)
            return DurableInputReplayStatus.GenerationMismatch;

        throw RelationalDurableInputRows.Corrupt(
            replay.Key,
            "replay compare-and-set did not update the matching dead-letter generation");
    }

    private static DurableInputDeadLetterSummary ReadDeadLetterSummary(SqlDataReader reader)
    {
        DurableInputKey? key = null;
        try
        {
            key = new DurableInputKey(
                ApplicationAddress.Parse(reader.GetString(0)),
                new FluxFlow.Nodes.MessageId(reader.GetString(1)));
            return new DurableInputDeadLetterSummary(
                key.Value,
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetBoolean(4),
                RelationalDurableInputRows.FromStoredTime(
                    reader.GetInt64(5),
                    reader.GetInt16(6),
                    key.Value,
                    "enqueue timestamp"),
                RelationalDurableInputRows.ReadPositiveInt32(
                    reader,
                    7,
                    key.Value,
                    "attempt"),
                RelationalDurableInputRows.ReadFailureKind(reader, 8, key.Value),
                RelationalDurableInputRows.ReadUtcTime(
                    reader,
                    9,
                    key.Value,
                    "dead-letter timestamp"),
                RelationalDurableInputRows.ReadPositiveInt64(
                    reader,
                    10,
                    key.Value,
                    "dead-letter generation"));
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
                    ? "Relational durable-input dead-letter row contains an invalid key."
                    : $"Relational durable-input row '{key}' contains invalid dead-letter metadata.",
                exception);
        }
    }
}
