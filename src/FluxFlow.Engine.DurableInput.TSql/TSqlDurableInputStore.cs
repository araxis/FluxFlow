using System.Data;
using Microsoft.Data.SqlClient;

namespace FluxFlow.Engine.DurableInput.TSql;

/// <summary>
/// Networked T-SQL implementation of the durable-input persistence capabilities.
/// </summary>
public sealed partial class TSqlDurableInputStore :
    IDurableInputStore,
    IDurableInputDeadLetterStore,
    IDurableInputLeaseRenewalStore,
    IDurableInputStatusStore,
    IDurableInputRetentionStore,
    IAsyncDisposable
{
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly TSqlDurableInputStoreSettings _settings;
    private int _initialized;
    private int _disposed;

    public TSqlDurableInputStore(TSqlDurableInputStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _settings = options.Resolve();
    }

    public async ValueTask<DurableInputEnqueueResult> EnqueueAsync(
        DurableInputEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        RelationalDurableInputRows.ValidateEnvelope(envelope);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            .ConfigureAwait(false);

        var existing = await ReadEnvelopeAsync(
                connection,
                transaction,
                envelope.Key,
                lockKey: true,
                cancellationToken)
            .ConfigureAwait(false);

        DurableInputEnqueueStatus status;
        if (existing is null)
        {
            await InsertAsync(connection, transaction, envelope, cancellationToken)
                .ConfigureAwait(false);
            status = DurableInputEnqueueStatus.Enqueued;
        }
        else
        {
            status = existing.HasSameContent(envelope)
                ? DurableInputEnqueueStatus.AlreadyExists
                : DurableInputEnqueueStatus.Conflict;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return new DurableInputEnqueueResult(envelope.Key, status);
    }

    public async ValueTask<IReadOnlyList<DurableInputLease>> LeaseAsync(
        DurableInputLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        RelationalDurableInputRows.ValidateLeaseRequest(request);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken)
            .ConfigureAwait(false);
        await using var command = CreateCommand(connection, transaction);
        command.CommandText = """
            ;WITH candidates AS (
                SELECT TOP (@maxCount) *
                FROM dbo.fluxflow_relational_inputs
                    WITH (UPDLOCK, READPAST, ROWLOCK, INDEX(ix_fluxflow_relational_inputs_eligibility))
                WHERE (state = @pendingState AND next_attempt_utc_ticks <= @now)
                   OR (state = @leasedState AND lease_until_utc_ticks <= @now)
                ORDER BY CASE state
                             WHEN @pendingState THEN next_attempt_utc_ticks
                             ELSE lease_until_utc_ticks
                         END,
                         enqueued_at_utc_ticks,
                         application_address COLLATE Latin1_General_100_BIN2,
                         message_id COLLATE Latin1_General_100_BIN2
            )
            UPDATE candidates
            SET state = @leasedState,
                attempt = attempt + 1,
                next_attempt_utc_ticks = NULL,
                lease_owner = @leaseOwner,
                lease_token = NEWID(),
                leased_at_utc_ticks = @leasedAt,
                lease_until_utc_ticks = @leaseUntil,
                delivered_at_utc_ticks = NULL,
                dead_lettered_at_utc_ticks = NULL
            OUTPUT CASE deleted.state
                       WHEN 0 THEN deleted.next_attempt_utc_ticks
                       ELSE deleted.lease_until_utc_ticks
                   END,
                   inserted.application_address,
                   inserted.message_id,
                   inserted.contract_name,
                   inserted.envelope_schema_version,
                   inserted.is_error,
                   inserted.payload_json,
                   inserted.error_code,
                   inserted.error_message,
                   inserted.error_category,
                   inserted.error_is_transient,
                   inserted.error_details_json,
                   inserted.trace_id,
                   inserted.correlation_id,
                   inserted.causation_id,
                   inserted.message_timestamp_utc_ticks,
                   inserted.message_timestamp_offset_minutes,
                   inserted.enqueued_at_utc_ticks,
                   inserted.enqueued_at_offset_minutes,
                   inserted.headers_json,
                   inserted.lease_token,
                   inserted.lease_owner,
                   inserted.leased_at_utc_ticks,
                   inserted.lease_until_utc_ticks,
                   inserted.attempt;
            """;
        RelationalDurableInputRows.Add(
            command,
            "@maxCount",
            SqlDbType.Int,
            request.MaxCount);
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
        RelationalDurableInputRows.Add(command, "@now", SqlDbType.BigInt, request.Now.UtcTicks);
        RelationalDurableInputRows.AddNVarChar(
            command,
            "@leaseOwner",
            request.OwnerId,
            RelationalDurableInputRows.LeaseOwnerMaxLength);
        RelationalDurableInputRows.Add(
            command,
            "@leasedAt",
            SqlDbType.BigInt,
            request.Now.UtcTicks);
        RelationalDurableInputRows.Add(
            command,
            "@leaseUntil",
            SqlDbType.BigInt,
            request.LeaseUntil.UtcTicks);

        var candidates = new List<LeasedCandidate>(request.MaxCount);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken)
                         .ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var due = reader.GetInt64(0);
                var envelope = RelationalDurableInputRows.ReadEnvelope(reader, 1);
                var token = reader.GetGuid(20);
                var owner = reader.GetString(21);
                var leasedAt = RelationalDurableInputRows.ReadUtcTime(
                    reader,
                    22,
                    envelope.Key,
                    "lease timestamp");
                var leaseUntil = RelationalDurableInputRows.ReadUtcTime(
                    reader,
                    23,
                    envelope.Key,
                    "lease expiry");
                var attempt = RelationalDurableInputRows.ReadPositiveInt32(
                    reader,
                    24,
                    envelope.Key,
                    "attempt");
                candidates.Add(new LeasedCandidate(
                    due,
                    new DurableInputLease(
                        envelope,
                        token,
                        owner,
                        leasedAt,
                        leaseUntil,
                        attempt)));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return candidates
            .OrderBy(static candidate => candidate.DueUtcTicks)
            .ThenBy(static candidate => candidate.Lease.Envelope.EnqueuedAt.UtcTicks)
            .ThenBy(
                static candidate => candidate.Lease.Envelope.Address.Value,
                StringComparer.Ordinal)
            .ThenBy(
                static candidate => candidate.Lease.Envelope.MessageId.Value,
                StringComparer.Ordinal)
            .Select(static candidate => candidate.Lease)
            .ToArray();
    }

    public ValueTask<DurableInputTransitionResult> MarkDeliveredAsync(
        DurableInputLeaseTransition transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);
        cancellationToken.ThrowIfCancellationRequested();
        RelationalDurableInputRows.ValidateKey(transition.Key);
        return ApplyTransitionAsync(
            transition.Key,
            transition.LeaseToken,
            transition.OccurredAt,
            """
            UPDATE dbo.fluxflow_relational_inputs
            SET state = @newState,
                next_attempt_utc_ticks = NULL,
                lease_owner = NULL,
                lease_token = NULL,
                leased_at_utc_ticks = NULL,
                lease_until_utc_ticks = NULL,
                delivered_at_utc_ticks = @occurredAt,
                dead_lettered_at_utc_ticks = NULL
            WHERE application_address = @address COLLATE Latin1_General_100_BIN2
              AND message_id = @messageId COLLATE Latin1_General_100_BIN2
              AND state = @leasedState
              AND lease_token = @leaseToken
              AND lease_until_utc_ticks > @occurredAt;
            """,
            DurableInputState.Delivered,
            failure: null,
            nextAttemptAt: null,
            cancellationToken);
    }

    public async ValueTask<DurableInputTransitionResult> RenewLeaseAsync(
        DurableInputLeaseRenewal renewal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renewal);
        cancellationToken.ThrowIfCancellationRequested();
        RelationalDurableInputRows.ValidateKey(renewal.Key);
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
            SET lease_until_utc_ticks = @leaseUntil
            WHERE application_address = @address COLLATE Latin1_General_100_BIN2
              AND message_id = @messageId COLLATE Latin1_General_100_BIN2
              AND state = @leasedState
              AND lease_token = @leaseToken
              AND lease_until_utc_ticks > @renewedAt;
            """;
        RelationalDurableInputRows.AddKey(command, renewal.Key);
        RelationalDurableInputRows.Add(
            command,
            "@leasedState",
            SqlDbType.TinyInt,
            (byte)DurableInputState.Leased);
        RelationalDurableInputRows.Add(
            command,
            "@leaseToken",
            SqlDbType.UniqueIdentifier,
            renewal.LeaseToken);
        RelationalDurableInputRows.Add(
            command,
            "@renewedAt",
            SqlDbType.BigInt,
            renewal.RenewedAt.UtcTicks);
        RelationalDurableInputRows.Add(
            command,
            "@leaseUntil",
            SqlDbType.BigInt,
            renewal.LeaseUntil.UtcTicks);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var status = affected == 1
            ? DurableInputTransitionStatus.Applied
            : await ResolveTransitionStatusAsync(
                    connection,
                    transaction,
                    renewal.Key,
                    cancellationToken)
                .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return new DurableInputTransitionResult(renewal.Key, status);
    }

    public ValueTask<DurableInputTransitionResult> ReleaseAsync(
        DurableInputRelease release,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        cancellationToken.ThrowIfCancellationRequested();
        RelationalDurableInputRows.ValidateKey(release.Key);
        return ApplyTransitionAsync(
            release.Key,
            release.LeaseToken,
            release.ReleasedAt,
            """
            UPDATE dbo.fluxflow_relational_inputs
            SET state = @newState,
                next_attempt_utc_ticks = @nextAttemptAt,
                lease_owner = NULL,
                lease_token = NULL,
                leased_at_utc_ticks = NULL,
                lease_until_utc_ticks = NULL,
                failure_kind = @failureKind,
                failure_description = @failureDescription,
                delivered_at_utc_ticks = NULL,
                dead_lettered_at_utc_ticks = NULL
            WHERE application_address = @address COLLATE Latin1_General_100_BIN2
              AND message_id = @messageId COLLATE Latin1_General_100_BIN2
              AND state = @leasedState
              AND lease_token = @leaseToken
              AND lease_until_utc_ticks > @occurredAt;
            """,
            DurableInputState.Pending,
            release.Failure,
            release.NextAttemptAt,
            cancellationToken);
    }

    public ValueTask<DurableInputTransitionResult> DeadLetterAsync(
        DurableInputDeadLetter deadLetter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deadLetter);
        cancellationToken.ThrowIfCancellationRequested();
        RelationalDurableInputRows.ValidateKey(deadLetter.Key);
        return ApplyTransitionAsync(
            deadLetter.Key,
            deadLetter.LeaseToken,
            deadLetter.DeadLetteredAt,
            """
            UPDATE dbo.fluxflow_relational_inputs
            SET state = @newState,
                next_attempt_utc_ticks = NULL,
                lease_owner = NULL,
                lease_token = NULL,
                leased_at_utc_ticks = NULL,
                lease_until_utc_ticks = NULL,
                failure_kind = @failureKind,
                failure_description = @failureDescription,
                delivered_at_utc_ticks = NULL,
                dead_lettered_at_utc_ticks = @occurredAt,
                dead_letter_generation = dead_letter_generation + 1
            WHERE application_address = @address COLLATE Latin1_General_100_BIN2
              AND message_id = @messageId COLLATE Latin1_General_100_BIN2
              AND state = @leasedState
              AND lease_token = @leaseToken
              AND lease_until_utc_ticks > @occurredAt;
            """,
            DurableInputState.DeadLettered,
            deadLetter.Failure,
            nextAttemptAt: null,
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _initializationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Connections are operation-scoped. The store owns no shared pool or server resource.
        }
        finally
        {
            _initializationGate.Release();
            _initializationGate.Dispose();
        }
    }

    private async ValueTask<DurableInputTransitionResult> ApplyTransitionAsync(
        DurableInputKey key,
        Guid leaseToken,
        DateTimeOffset occurredAt,
        string commandText,
        DurableInputState newState,
        DurableInputFailure? failure,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            .ConfigureAwait(false);
        await using var command = CreateCommand(connection, transaction);
        command.CommandText = commandText;
        RelationalDurableInputRows.AddKey(command, key);
        RelationalDurableInputRows.Add(
            command,
            "@leaseToken",
            SqlDbType.UniqueIdentifier,
            leaseToken);
        RelationalDurableInputRows.Add(
            command,
            "@occurredAt",
            SqlDbType.BigInt,
            occurredAt.UtcTicks);
        RelationalDurableInputRows.Add(
            command,
            "@newState",
            SqlDbType.TinyInt,
            (byte)newState);
        RelationalDurableInputRows.Add(
            command,
            "@leasedState",
            SqlDbType.TinyInt,
            (byte)DurableInputState.Leased);
        RelationalDurableInputRows.Add(
            command,
            "@nextAttemptAt",
            SqlDbType.BigInt,
            nextAttemptAt?.UtcTicks);
        RelationalDurableInputRows.Add(
            command,
            "@failureKind",
            SqlDbType.Int,
            failure is null ? null : (int)failure.Kind);
        RelationalDurableInputRows.AddNVarChar(
            command,
            "@failureDescription",
            failure?.Description,
            -1);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var status = affected == 1
            ? DurableInputTransitionStatus.Applied
            : await ResolveTransitionStatusAsync(
                    connection,
                    transaction,
                    key,
                    cancellationToken)
                .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return new DurableInputTransitionResult(key, status);
    }

    private async ValueTask<DurableInputTransitionStatus> ResolveTransitionStatusAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DurableInputKey key,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction);
        command.CommandText = """
            SELECT state
            FROM dbo.fluxflow_relational_inputs WITH (UPDLOCK, HOLDLOCK)
            WHERE application_address = @address COLLATE Latin1_General_100_BIN2
              AND message_id = @messageId COLLATE Latin1_General_100_BIN2;
            """;
        RelationalDurableInputRows.AddKey(command, key);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null or DBNull)
            return DurableInputTransitionStatus.NotFound;

        var stateValue = Convert.ToInt32(
            value,
            System.Globalization.CultureInfo.InvariantCulture);
        if (!Enum.IsDefined(typeof(DurableInputState), stateValue))
            throw RelationalDurableInputRows.Corrupt(key, $"state value {stateValue} is invalid");

        return (DurableInputState)stateValue == DurableInputState.Leased
            ? DurableInputTransitionStatus.LeaseLost
            : DurableInputTransitionStatus.InvalidState;
    }

    private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _initialized) != 0)
            return;

        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_initialized != 0)
                return;

            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await RelationalDurableInputSchema.InitializeAsync(
                    connection,
                    _settings,
                    cancellationToken)
                .ConfigureAwait(false);
            Volatile.Write(ref _initialized, 1);
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async ValueTask<SqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var connection = new SqlConnection(_settings.NormalizedConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask InsertAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DurableInputEnvelope envelope,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction);
        command.CommandText = """
            INSERT INTO dbo.fluxflow_relational_inputs (
                application_address,
                message_id,
                contract_name,
                envelope_schema_version,
                is_error,
                payload_json,
                error_code,
                error_message,
                error_category,
                error_is_transient,
                error_details_json,
                trace_id,
                correlation_id,
                causation_id,
                message_timestamp_utc_ticks,
                message_timestamp_offset_minutes,
                enqueued_at_utc_ticks,
                enqueued_at_offset_minutes,
                headers_json,
                state,
                attempt,
                next_attempt_utc_ticks,
                dead_letter_generation)
            VALUES (
                @address,
                @messageId,
                @contractName,
                @schemaVersion,
                @isError,
                @payloadJson,
                @errorCode,
                @errorMessage,
                @errorCategory,
                @errorIsTransient,
                @errorDetailsJson,
                @traceId,
                @correlationId,
                @causationId,
                @messageTimestamp,
                @messageTimestampOffset,
                @enqueuedAt,
                @enqueuedAtOffset,
                @headersJson,
                @state,
                0,
                @nextAttemptAt,
                0);
            """;
        RelationalDurableInputRows.AddEnvelopeParameters(command, envelope);
        RelationalDurableInputRows.Add(
            command,
            "@state",
            SqlDbType.TinyInt,
            (byte)DurableInputState.Pending);
        RelationalDurableInputRows.Add(
            command,
            "@nextAttemptAt",
            SqlDbType.BigInt,
            envelope.EnqueuedAt.UtcTicks);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
            throw new InvalidDataException("Relational durable-input enqueue did not insert exactly one row.");
    }

    private async ValueTask<DurableInputEnvelope?> ReadEnvelopeAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DurableInputKey key,
        bool lockKey,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction);
        var hints = lockKey ? "WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        command.CommandText = $"""
            SELECT {RelationalDurableInputRows.EnvelopeColumns}
            FROM dbo.fluxflow_relational_inputs AS i {hints}
            WHERE i.application_address = @address COLLATE Latin1_General_100_BIN2
              AND i.message_id = @messageId COLLATE Latin1_General_100_BIN2;
            """;
        RelationalDurableInputRows.AddKey(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? RelationalDurableInputRows.ReadEnvelope(reader)
            : null;
    }

    private SqlCommand CreateCommand(
        SqlConnection connection,
        SqlTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = _settings.CommandTimeoutSeconds;
        return command;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed record LeasedCandidate(long DueUtcTicks, DurableInputLease Lease);
}
