using System.Data;
using Microsoft.Data.SqlClient;

namespace FluxFlow.Engine.DurableOutput.TSql;

public sealed partial class TSqlDurableOutputStore
{
    public async ValueTask<DurableOutputDeliveryLease?> TryLeaseAsync(
        DurableOutputDeliveryLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        RelationalDurableOutputRows.ValidateLeaseOwner(request.OwnerId);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken)
            .ConfigureAwait(false);

        await BackfillPendingAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var token = Guid.NewGuid();
        var key = await AssignNextLeaseAsync(
                connection,
                transaction,
                token,
                request,
                cancellationToken)
            .ConfigureAwait(false);

        DurableOutputDeliveryLease? lease = null;
        if (key is { } leasedKey)
        {
            lease = await ReadLeaseAsync(
                    connection,
                    transaction,
                    leasedKey,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return lease;
    }

    public ValueTask<DurableOutputDeliveryTransitionResult> CompleteAsync(
        DurableOutputDeliveryTransition transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);
        return ApplyLeaseTransitionAsync(
            transition.Key,
            transition.LeaseToken,
            transition.OccurredAt,
            """
                UPDATE dbo.fluxflow_relational_output_deliveries
                SET state = 3,
                    lease_token = NULL,
                    lease_owner = NULL,
                    leased_at_utc_ticks = NULL,
                    leased_at_offset_minutes = NULL,
                    lease_until_utc_ticks = NULL,
                    lease_until_offset_minutes = NULL,
                    delivered_at_utc_ticks = @occurredAt,
                    delivered_at_offset_minutes = @occurredAtOffset,
                    dead_letter_reason = NULL,
                    dead_lettered_at_utc_ticks = NULL,
                    dead_lettered_at_offset_minutes = NULL
                WHERE application_address = @address COLLATE Latin1_General_100_BIN2
                  AND message_id = @messageId COLLATE Latin1_General_100_BIN2
                  AND state = 2
                  AND lease_token = @leaseToken
                  AND lease_until_utc_ticks > @occurredAt;
                """,
            configure: null,
            cancellationToken);
    }

    public ValueTask<DurableOutputDeliveryTransitionResult> RenewLeaseAsync(
        DurableOutputDeliveryLeaseRenewal renewal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renewal);
        return ApplyLeaseTransitionAsync(
            renewal.Key,
            renewal.LeaseToken,
            renewal.RenewedAt,
            """
                UPDATE dbo.fluxflow_relational_output_deliveries
                SET lease_until_utc_ticks = @leaseUntil,
                    lease_until_offset_minutes = @leaseUntilOffset
                WHERE application_address = @address COLLATE Latin1_General_100_BIN2
                  AND message_id = @messageId COLLATE Latin1_General_100_BIN2
                  AND state = 2
                  AND lease_token = @leaseToken
                  AND lease_until_utc_ticks > @occurredAt;
                """,
            command =>
            {
                RelationalDurableOutputRows.Add(
                    command,
                    "@leaseUntil",
                    SqlDbType.BigInt,
                    renewal.LeaseUntil.UtcTicks);
                RelationalDurableOutputRows.Add(
                    command,
                    "@leaseUntilOffset",
                    SqlDbType.SmallInt,
                    RelationalDurableOutputRows.ToOffsetMinutes(renewal.LeaseUntil));
            },
            cancellationToken);
    }

    public ValueTask<DurableOutputDeliveryTransitionResult> RetryAsync(
        DurableOutputDeliveryRetry retry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(retry);
        return ApplyLeaseTransitionAsync(
            retry.Key,
            retry.LeaseToken,
            retry.ReleasedAt,
            """
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
                    delivered_at_utc_ticks = NULL,
                    delivered_at_offset_minutes = NULL,
                    dead_letter_reason = NULL,
                    dead_lettered_at_utc_ticks = NULL,
                    dead_lettered_at_offset_minutes = NULL
                WHERE application_address = @address COLLATE Latin1_General_100_BIN2
                  AND message_id = @messageId COLLATE Latin1_General_100_BIN2
                  AND state = 2
                  AND lease_token = @leaseToken
                  AND lease_until_utc_ticks > @occurredAt;
                """,
            command =>
            {
                RelationalDurableOutputRows.Add(
                    command,
                    "@nextAttemptAt",
                    SqlDbType.BigInt,
                    retry.NextAttemptAt.UtcTicks);
                RelationalDurableOutputRows.Add(
                    command,
                    "@nextAttemptAtOffset",
                    SqlDbType.SmallInt,
                    RelationalDurableOutputRows.ToOffsetMinutes(retry.NextAttemptAt));
            },
            cancellationToken);
    }

    public ValueTask<DurableOutputDeliveryTransitionResult> DeadLetterAsync(
        DurableOutputDeliveryDeadLetter deadLetter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deadLetter);
        return ApplyLeaseTransitionAsync(
            deadLetter.Key,
            deadLetter.LeaseToken,
            deadLetter.DeadLetteredAt,
            """
                UPDATE dbo.fluxflow_relational_output_deliveries
                SET state = 4,
                    lease_token = NULL,
                    lease_owner = NULL,
                    leased_at_utc_ticks = NULL,
                    leased_at_offset_minutes = NULL,
                    lease_until_utc_ticks = NULL,
                    lease_until_offset_minutes = NULL,
                    delivered_at_utc_ticks = NULL,
                    delivered_at_offset_minutes = NULL,
                    dead_letter_reason = @deadLetterReason,
                    dead_lettered_at_utc_ticks = @occurredAt,
                    dead_lettered_at_offset_minutes = @occurredAtOffset,
                    dead_letter_generation = dead_letter_generation + 1
                WHERE application_address = @address COLLATE Latin1_General_100_BIN2
                  AND message_id = @messageId COLLATE Latin1_General_100_BIN2
                  AND state = 2
                  AND lease_token = @leaseToken
                  AND lease_until_utc_ticks > @occurredAt;
                """,
            command => RelationalDurableOutputRows.Add(
                command,
                "@deadLetterReason",
                SqlDbType.Int,
                (int)deadLetter.Reason),
            cancellationToken);
    }

    private async ValueTask BackfillPendingAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction);
        command.CommandText = """
            INSERT INTO dbo.fluxflow_relational_output_deliveries (
                application_address,
                message_id,
                state,
                next_attempt_utc_ticks,
                next_attempt_offset_minutes,
                attempt,
                dead_letter_generation)
            SELECT o.application_address,
                   o.message_id,
                   1,
                   o.captured_at_utc_ticks,
                   o.captured_at_offset_minutes,
                   0,
                   0
            FROM dbo.fluxflow_relational_outputs AS o WITH (HOLDLOCK)
            WHERE NOT EXISTS (
                SELECT 1
                FROM dbo.fluxflow_relational_output_deliveries AS d WITH (UPDLOCK, HOLDLOCK)
                WHERE d.application_address = o.application_address
                  AND d.message_id = o.message_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<DurableOutputKey?> AssignNextLeaseAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid token,
        DurableOutputDeliveryLeaseRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction);
        command.CommandText = """
            DECLARE @candidateAddress nvarchar(300);
            DECLARE @candidateMessageId nvarchar(128);

            SELECT TOP (1)
                @candidateAddress = d.application_address,
                @candidateMessageId = d.message_id
            FROM dbo.fluxflow_relational_output_deliveries AS d WITH (UPDLOCK, READPAST, ROWLOCK)
            INNER JOIN dbo.fluxflow_relational_outputs AS o
              ON o.application_address = d.application_address
             AND o.message_id = d.message_id
            WHERE (d.state = 1 AND d.next_attempt_utc_ticks <= @leasedAt)
               OR (d.state = 2 AND d.lease_until_utc_ticks <= @leasedAt)
            ORDER BY o.captured_at_utc_ticks,
                     d.application_address COLLATE Latin1_General_100_BIN2,
                     d.message_id COLLATE Latin1_General_100_BIN2;

            UPDATE dbo.fluxflow_relational_output_deliveries
            SET state = 2,
                lease_token = @leaseToken,
                lease_owner = @leaseOwner,
                leased_at_utc_ticks = @leasedAt,
                leased_at_offset_minutes = @leasedAtOffset,
                lease_until_utc_ticks = @leaseUntil,
                lease_until_offset_minutes = @leaseUntilOffset,
                attempt = attempt + 1,
                delivered_at_utc_ticks = NULL,
                delivered_at_offset_minutes = NULL,
                dead_letter_reason = NULL,
                dead_lettered_at_utc_ticks = NULL,
                dead_lettered_at_offset_minutes = NULL
            OUTPUT inserted.application_address, inserted.message_id
            WHERE application_address = @candidateAddress
              AND message_id = @candidateMessageId
              AND ((state = 1 AND next_attempt_utc_ticks <= @leasedAt)
                OR (state = 2 AND lease_until_utc_ticks <= @leasedAt));
            """;
        RelationalDurableOutputRows.Add(command, "@leaseToken", SqlDbType.UniqueIdentifier, token);
        RelationalDurableOutputRows.AddNVarChar(command, "@leaseOwner", request.OwnerId, 512);
        RelationalDurableOutputRows.Add(command, "@leasedAt", SqlDbType.BigInt, request.Now.UtcTicks);
        RelationalDurableOutputRows.Add(
            command,
            "@leasedAtOffset",
            SqlDbType.SmallInt,
            RelationalDurableOutputRows.ToOffsetMinutes(request.Now));
        RelationalDurableOutputRows.Add(command, "@leaseUntil", SqlDbType.BigInt, request.LeaseUntil.UtcTicks);
        RelationalDurableOutputRows.Add(
            command,
            "@leaseUntilOffset",
            SqlDbType.SmallInt,
            RelationalDurableOutputRows.ToOffsetMinutes(request.LeaseUntil));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        var key = new DurableOutputKey(
            FluxFlow.Composition.Addressing.ApplicationAddress.Parse(reader.GetString(0)),
            new FluxFlow.Nodes.MessageId(reader.GetString(1)));
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("Relational durable-output lease selected more than one row.");
        return key;
    }

    private async ValueTask<DurableOutputDeliveryLease> ReadLeaseAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DurableOutputKey key,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction);
        command.CommandText = $"""
            SELECT {RelationalDurableOutputRows.EnvelopeColumns},
                   d.lease_token,
                   d.lease_owner,
                   d.leased_at_utc_ticks,
                   d.leased_at_offset_minutes,
                   d.lease_until_utc_ticks,
                   d.lease_until_offset_minutes,
                   d.attempt
            FROM dbo.fluxflow_relational_output_deliveries AS d
            INNER JOIN dbo.fluxflow_relational_outputs AS o
              ON o.application_address = d.application_address
             AND o.message_id = d.message_id
            WHERE d.application_address = @address COLLATE Latin1_General_100_BIN2
              AND d.message_id = @messageId COLLATE Latin1_General_100_BIN2
              AND d.state = 2;
            """;
        RelationalDurableOutputRows.AddKey(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException($"Relational durable-output delivery lease '{key}' is missing.");

        var envelope = RelationalDurableOutputRows.ReadEnvelope(reader);
        var token = reader.GetGuid(19);
        var owner = reader.GetString(20);
        var leasedAt = RelationalDurableOutputRows.ReadStoredTime(reader, 21, 22, key, "leased-at timestamp");
        var leaseUntil = RelationalDurableOutputRows.ReadStoredTime(reader, 23, 24, key, "lease-until timestamp");
        var attempt = reader.GetInt32(25);
        if (token == Guid.Empty || string.IsNullOrWhiteSpace(owner) || attempt <= 0)
            throw RelationalDurableOutputRows.Corrupt(key, "lease metadata is invalid");
        return new DurableOutputDeliveryLease(envelope, token, owner, leasedAt, leaseUntil, attempt);
    }

    private async ValueTask<DurableOutputDeliveryTransitionResult> ApplyLeaseTransitionAsync(
        DurableOutputKey key,
        Guid leaseToken,
        DateTimeOffset occurredAt,
        string updateSql,
        Action<SqlCommand>? configure,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken)
                .ConfigureAwait(false);

            await using var command = CreateCommand(connection, transaction);
            command.CommandText = updateSql;
            RelationalDurableOutputRows.AddKey(command, key);
            RelationalDurableOutputRows.Add(
                command,
                "@leaseToken",
                SqlDbType.UniqueIdentifier,
                leaseToken);
            RelationalDurableOutputRows.Add(
                command,
                "@occurredAt",
                SqlDbType.BigInt,
                occurredAt.UtcTicks);
            RelationalDurableOutputRows.Add(
                command,
                "@occurredAtOffset",
                SqlDbType.SmallInt,
                RelationalDurableOutputRows.ToOffsetMinutes(occurredAt));
            configure?.Invoke(command);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var status = affected == 1
                ? DurableOutputDeliveryTransitionStatus.Applied
                : await ResolveTransitionStatusAsync(
                        connection,
                        transaction,
                        key,
                        leaseToken,
                        occurredAt,
                        cancellationToken)
                    .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return new DurableOutputDeliveryTransitionResult(key, status);
        }
        catch (SqlException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private async ValueTask<DurableOutputDeliveryTransitionStatus> ResolveTransitionStatusAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DurableOutputKey key,
        Guid leaseToken,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction);
        command.CommandText = """
            SELECT state, lease_token, lease_until_utc_ticks
            FROM dbo.fluxflow_relational_output_deliveries WITH (UPDLOCK, HOLDLOCK)
            WHERE application_address = @address COLLATE Latin1_General_100_BIN2
              AND message_id = @messageId COLLATE Latin1_General_100_BIN2;
            """;
        RelationalDurableOutputRows.AddKey(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return DurableOutputDeliveryTransitionStatus.NotFound;

        var state = reader.GetByte(0);
        if (state != DeliveryLeased)
            return DurableOutputDeliveryTransitionStatus.InvalidState;
        if (reader.IsDBNull(1) || reader.IsDBNull(2))
            throw RelationalDurableOutputRows.Corrupt(key, "leased state is incomplete");
        if (reader.GetGuid(1) != leaseToken || reader.GetInt64(2) <= occurredAt.UtcTicks)
            return DurableOutputDeliveryTransitionStatus.LeaseLost;
        throw RelationalDurableOutputRows.Corrupt(key, "matching lease transition compare-and-set did not update");
    }
}
