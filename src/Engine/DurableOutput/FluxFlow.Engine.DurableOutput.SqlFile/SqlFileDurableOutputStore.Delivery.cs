using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Data.Sqlite;

namespace FluxFlow.Engine.DurableOutput.SqlFile;

public sealed partial class SqlFileDurableOutputStore
{
    private const int DeliveryPending = 1;
    private const int DeliveryLeased = 2;
    private const int DeliveryCompleted = 3;
    private const int DeliveryDeadLettered = 4;

    private int _deliveryInitialized;

    public async ValueTask<DurableOutputDeliveryLease?> TryLeaseAsync(
        DurableOutputDeliveryLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureDeliveryInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var transaction = BeginWriteTransaction(connection);

            await InitializePendingDeliveriesAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            var key = await FindEligibleKeyAsync(
                    connection,
                    transaction,
                    request.Now,
                    cancellationToken)
                .ConfigureAwait(false);
            if (key is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                return null;
            }

            var token = Guid.NewGuid();
            await AssignLeaseAsync(
                    connection,
                    transaction,
                    key.Value,
                    token,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            var lease = await ReadLeaseAsync(
                    connection,
                    transaction,
                    key.Value,
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return lease;
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("delivery lease", exception);
        }
    }

    public async ValueTask<DurableOutputDeliveryTransitionResult> RenewLeaseAsync(
        DurableOutputDeliveryLeaseRenewal renewal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renewal);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureDeliveryInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var transaction = BeginWriteTransaction(connection);
            var affected = await RenewLeaseAsync(
                    connection,
                    transaction,
                    renewal,
                    cancellationToken)
                .ConfigureAwait(false);
            var status = affected == 1
                ? DurableOutputDeliveryTransitionStatus.Applied
                : await ReadTransitionStatusAsync(
                        connection,
                        transaction,
                        renewal.Key,
                        renewal.LeaseToken,
                        renewal.RenewedAt,
                        cancellationToken)
                    .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return new DurableOutputDeliveryTransitionResult(renewal.Key, status);
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("delivery lease renewal", exception);
        }
    }

    public async ValueTask<DurableOutputDeliveryTransitionResult> CompleteAsync(
        DurableOutputDeliveryTransition transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);
        await EnsureDeliveryInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var transaction = BeginWriteTransaction(connection);
            var affected = await CompleteLeaseAsync(
                    connection,
                    transaction,
                    transition,
                    cancellationToken)
                .ConfigureAwait(false);
            var status = affected == 1
                ? DurableOutputDeliveryTransitionStatus.Applied
                : await ReadTransitionStatusAsync(
                        connection,
                        transaction,
                        transition.Key,
                        transition.LeaseToken,
                        transition.OccurredAt,
                        cancellationToken)
                    .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return new DurableOutputDeliveryTransitionResult(transition.Key, status);
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("delivery completion", exception);
        }
    }

    public async ValueTask<DurableOutputDeliveryTransitionResult> RetryAsync(
        DurableOutputDeliveryRetry retry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(retry);
        await EnsureDeliveryInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var transaction = BeginWriteTransaction(connection);
            var affected = await RetryLeaseAsync(
                    connection,
                    transaction,
                    retry,
                    cancellationToken)
                .ConfigureAwait(false);
            var status = affected == 1
                ? DurableOutputDeliveryTransitionStatus.Applied
                : await ReadTransitionStatusAsync(
                        connection,
                        transaction,
                        retry.Key,
                        retry.LeaseToken,
                        retry.ReleasedAt,
                        cancellationToken)
                    .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return new DurableOutputDeliveryTransitionResult(retry.Key, status);
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("delivery retry", exception);
        }
    }

    public async ValueTask<DurableOutputDeliveryTransitionResult> DeadLetterAsync(
        DurableOutputDeliveryDeadLetter deadLetter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deadLetter);
        await EnsureDeliveryInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var transaction = BeginWriteTransaction(connection);
            var affected = await DeadLetterLeaseAsync(
                    connection,
                    transaction,
                    deadLetter,
                    cancellationToken)
                .ConfigureAwait(false);
            var status = affected == 1
                ? DurableOutputDeliveryTransitionStatus.Applied
                : await ReadTransitionStatusAsync(
                        connection,
                        transaction,
                        deadLetter.Key,
                        deadLetter.LeaseToken,
                        deadLetter.DeadLetteredAt,
                        cancellationToken)
                    .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return new DurableOutputDeliveryTransitionResult(deadLetter.Key, status);
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("delivery dead-letter", exception);
        }
    }

    private async ValueTask EnsureDeliveryInitializedAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (Volatile.Read(ref _deliveryInitialized) != 0)
            return;

        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_deliveryInitialized != 0)
                return;

            try
            {
                await using var connection = await OpenConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);
                await SqlFileDurableOutputDeliverySchema.InitializeAsync(
                        connection,
                        cancellationToken)
                    .ConfigureAwait(false);
                Volatile.Write(ref _deliveryInitialized, 1);
            }
            catch (SqliteException exception) when (IsBusy(exception))
            {
                throw CreateBusyException("delivery schema initialization", exception);
            }
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private static async ValueTask InitializePendingDeliveriesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO fluxflow_durable_output_deliveries (
                application_address,
                message_id,
                state,
                next_attempt_utc_ticks,
                next_attempt_offset_minutes,
                lease_token,
                lease_owner,
                leased_at_utc_ticks,
                leased_at_offset_minutes,
                lease_until_utc_ticks,
                lease_until_offset_minutes,
                attempt,
                delivered_at_utc_ticks,
                delivered_at_offset_minutes,
                dead_letter_reason,
                dead_lettered_at_utc_ticks,
                dead_lettered_at_offset_minutes,
                dead_letter_generation
            )
            SELECT
                o.application_address,
                o.message_id,
                1,
                o.captured_at_utc_ticks,
                o.captured_at_offset_minutes,
                NULL,
                NULL,
                NULL,
                NULL,
                NULL,
                NULL,
                0,
                NULL,
                NULL,
                NULL,
                NULL,
                NULL,
                0
            FROM fluxflow_durable_outputs AS o
            WHERE NOT EXISTS (
                SELECT 1
                FROM fluxflow_durable_output_deliveries AS d
                WHERE d.application_address = o.application_address
                  AND d.message_id = o.message_id
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<DurableOutputKey?> FindEligibleKeyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT d.application_address, d.message_id
            FROM fluxflow_durable_output_deliveries AS d
            INNER JOIN fluxflow_durable_outputs AS o
              ON o.application_address = d.application_address
             AND o.message_id = d.message_id
            WHERE (d.state = 1 AND d.next_attempt_utc_ticks <= $now)
               OR (d.state = 2 AND d.lease_until_utc_ticks <= $now)
            ORDER BY
                d.next_attempt_utc_ticks,
                o.captured_at_utc_ticks,
                d.application_address COLLATE BINARY,
                d.message_id COLLATE BINARY
            LIMIT 1;
            """;
        Add(command, "$now", ToUtcTicks(now));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        try
        {
            return new DurableOutputKey(
                ApplicationAddress.Parse(reader.GetString(0)),
                new MessageId(reader.GetString(1)));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new InvalidDataException(
                "SQL-file durable output delivery row contains an invalid key.",
                exception);
        }
    }

    private static async ValueTask AssignLeaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DurableOutputKey key,
        Guid token,
        DurableOutputDeliveryLeaseRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE fluxflow_durable_output_deliveries
            SET state = 2,
                lease_token = $leaseToken,
                lease_owner = $leaseOwner,
                leased_at_utc_ticks = $leasedAt,
                leased_at_offset_minutes = $leasedAtOffset,
                lease_until_utc_ticks = $leaseUntil,
                lease_until_offset_minutes = $leaseUntilOffset,
                attempt = attempt + 1,
                delivered_at_utc_ticks = NULL,
                delivered_at_offset_minutes = NULL,
                dead_letter_reason = NULL,
                dead_lettered_at_utc_ticks = NULL,
                dead_lettered_at_offset_minutes = NULL
            WHERE application_address = $address
              AND message_id = $messageId
              AND ((state = 1 AND next_attempt_utc_ticks <= $leasedAt)
                OR (state = 2 AND lease_until_utc_ticks <= $leasedAt));
            """;
        AddKey(command, key);
        Add(command, "$leaseToken", token.ToString("N"));
        Add(command, "$leaseOwner", request.OwnerId);
        Add(command, "$leasedAt", ToUtcTicks(request.Now));
        Add(command, "$leasedAtOffset", ToOffsetMinutes(request.Now));
        Add(command, "$leaseUntil", ToUtcTicks(request.LeaseUntil));
        Add(command, "$leaseUntilOffset", ToOffsetMinutes(request.LeaseUntil));
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            throw new InvalidDataException(
                $"SQL-file durable output delivery could not exclusively lease row '{key}'.");
        }
    }

    private static async ValueTask<DurableOutputDeliveryLease> ReadLeaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DurableOutputKey key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                {QualifiedEnvelopeColumns},
                d.lease_token,
                d.lease_owner,
                d.leased_at_utc_ticks,
                d.leased_at_offset_minutes,
                d.lease_until_utc_ticks,
                d.lease_until_offset_minutes,
                d.attempt
            FROM fluxflow_durable_output_deliveries AS d
            INNER JOIN fluxflow_durable_outputs AS o
              ON o.application_address = d.application_address
             AND o.message_id = d.message_id
            WHERE d.application_address = $address
              AND d.message_id = $messageId
              AND d.state = 2;
            """;
        AddKey(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException($"SQL-file durable output delivery lease '{key}' is missing.");

        try
        {
            var envelope = ReadEnvelope(reader);
            if (!Guid.TryParseExact(reader.GetString(19), "N", out var token) ||
                token == Guid.Empty)
            {
                throw new InvalidDataException(
                    $"SQL-file durable output delivery lease '{key}' contains an invalid token.");
            }

            return new DurableOutputDeliveryLease(
                envelope,
                token,
                reader.GetString(20),
                FromStoredTime(reader.GetInt64(21), reader.GetInt32(22), key, "leased-at timestamp"),
                FromStoredTime(reader.GetInt64(23), reader.GetInt32(24), key, "lease-until timestamp"),
                reader.GetInt32(25));
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
                $"SQL-file durable output delivery lease '{key}' is corrupt.",
                exception);
        }
    }

    private static async ValueTask<int> RenewLeaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DurableOutputDeliveryLeaseRenewal renewal,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE fluxflow_durable_output_deliveries
            SET lease_until_utc_ticks = $leaseUntil,
                lease_until_offset_minutes = $leaseUntilOffset
            WHERE application_address = $address
              AND message_id = $messageId
              AND state = 2
              AND lease_token = $leaseToken
              AND lease_until_utc_ticks > $renewedAt;
            """;
        AddKey(command, renewal.Key);
        Add(command, "$leaseToken", renewal.LeaseToken.ToString("N"));
        Add(command, "$renewedAt", ToUtcTicks(renewal.RenewedAt));
        Add(command, "$leaseUntil", ToUtcTicks(renewal.LeaseUntil));
        Add(command, "$leaseUntilOffset", ToOffsetMinutes(renewal.LeaseUntil));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<int> CompleteLeaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DurableOutputDeliveryTransition transition,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE fluxflow_durable_output_deliveries
            SET state = 3,
                lease_token = NULL,
                lease_owner = NULL,
                leased_at_utc_ticks = NULL,
                leased_at_offset_minutes = NULL,
                lease_until_utc_ticks = NULL,
                lease_until_offset_minutes = NULL,
                delivered_at_utc_ticks = $occurredAt,
                delivered_at_offset_minutes = $occurredAtOffset,
                dead_letter_reason = NULL,
                dead_lettered_at_utc_ticks = NULL,
                dead_lettered_at_offset_minutes = NULL
            WHERE application_address = $address
              AND message_id = $messageId
              AND state = 2
              AND lease_token = $leaseToken
              AND lease_until_utc_ticks > $occurredAt;
            """;
        AddKey(command, transition.Key);
        Add(command, "$leaseToken", transition.LeaseToken.ToString("N"));
        Add(command, "$occurredAt", ToUtcTicks(transition.OccurredAt));
        Add(command, "$occurredAtOffset", ToOffsetMinutes(transition.OccurredAt));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<int> RetryLeaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DurableOutputDeliveryRetry retry,
        CancellationToken cancellationToken)
    {
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
                delivered_at_utc_ticks = NULL,
                delivered_at_offset_minutes = NULL,
                dead_letter_reason = NULL,
                dead_lettered_at_utc_ticks = NULL,
                dead_lettered_at_offset_minutes = NULL
            WHERE application_address = $address
              AND message_id = $messageId
              AND state = 2
              AND lease_token = $leaseToken
              AND lease_until_utc_ticks > $releasedAt;
            """;
        AddKey(command, retry.Key);
        Add(command, "$leaseToken", retry.LeaseToken.ToString("N"));
        Add(command, "$releasedAt", ToUtcTicks(retry.ReleasedAt));
        Add(command, "$nextAttemptAt", ToUtcTicks(retry.NextAttemptAt));
        Add(command, "$nextAttemptAtOffset", ToOffsetMinutes(retry.NextAttemptAt));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<int> DeadLetterLeaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DurableOutputDeliveryDeadLetter deadLetter,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE fluxflow_durable_output_deliveries
            SET state = 4,
                lease_token = NULL,
                lease_owner = NULL,
                leased_at_utc_ticks = NULL,
                leased_at_offset_minutes = NULL,
                lease_until_utc_ticks = NULL,
                lease_until_offset_minutes = NULL,
                delivered_at_utc_ticks = NULL,
                delivered_at_offset_minutes = NULL,
                dead_letter_reason = $reason,
                dead_lettered_at_utc_ticks = $deadLetteredAt,
                dead_lettered_at_offset_minutes = $deadLetteredAtOffset,
                dead_letter_generation = dead_letter_generation + 1
            WHERE application_address = $address
              AND message_id = $messageId
              AND state = 2
              AND lease_token = $leaseToken
              AND lease_until_utc_ticks > $deadLetteredAt;
            """;
        AddKey(command, deadLetter.Key);
        Add(command, "$leaseToken", deadLetter.LeaseToken.ToString("N"));
        Add(command, "$reason", (int)deadLetter.Reason);
        Add(command, "$deadLetteredAt", ToUtcTicks(deadLetter.DeadLetteredAt));
        Add(command, "$deadLetteredAtOffset", ToOffsetMinutes(deadLetter.DeadLetteredAt));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<DurableOutputDeliveryTransitionStatus> ReadTransitionStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DurableOutputKey key,
        Guid leaseToken,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT state, lease_token, lease_until_utc_ticks
            FROM fluxflow_durable_output_deliveries
            WHERE application_address = $address
              AND message_id = $messageId;
            """;
        AddKey(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return DurableOutputDeliveryTransitionStatus.NotFound;

        var state = reader.GetInt32(0);
        if (state is DeliveryPending or DeliveryCompleted or DeliveryDeadLettered)
            return DurableOutputDeliveryTransitionStatus.InvalidState;
        if (state != DeliveryLeased || reader.IsDBNull(1) || reader.IsDBNull(2))
            throw new InvalidDataException($"SQL-file durable output delivery row '{key}' is corrupt.");

        if (!Guid.TryParseExact(reader.GetString(1), "N", out var currentToken) ||
            currentToken == Guid.Empty)
        {
            throw new InvalidDataException(
                $"SQL-file durable output delivery row '{key}' contains an invalid lease token.");
        }

        return currentToken != leaseToken || reader.GetInt64(2) <= ToUtcTicks(occurredAt)
            ? DurableOutputDeliveryTransitionStatus.LeaseLost
            : DurableOutputDeliveryTransitionStatus.InvalidState;
    }

    private const string QualifiedEnvelopeColumns = """
        o.application_address,
        o.message_id,
        o.contract_name,
        o.envelope_schema_version,
        o.is_error,
        o.payload_json,
        o.error_code,
        o.error_message,
        o.error_category,
        o.error_is_transient,
        o.error_details_json,
        o.trace_id,
        o.correlation_id,
        o.causation_id,
        o.message_timestamp_utc_ticks,
        o.message_timestamp_offset_minutes,
        o.captured_at_utc_ticks,
        o.captured_at_offset_minutes,
        o.headers_json
        """;
}
