using System.Data;
using System.Globalization;
using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Data.Sqlite;

namespace FluxFlow.Engine.DurableInput.SqlFile;

/// <summary>
/// SQLite single-file implementation of the durable input store contract.
/// </summary>
public sealed partial class SqlFileDurableInputStore :
    IDurableInputStore,
    IDurableInputDeadLetterStore,
    IDurableInputLeaseRenewalStore,
    IDurableInputStatusStore,
    IDurableInputRetentionStore,
    IAsyncDisposable
{
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SqlFileDurableInputStoreSettings _settings;
    private readonly string _connectionString;
    private int _initialized;
    private int _disposed;

    public SqlFileDurableInputStore(SqlFileDurableInputStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _settings = options.Resolve();
        _connectionString = CreateConnectionString(_settings);
    }

    public async ValueTask<DurableInputEnqueueResult> EnqueueAsync(
        DurableInputEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var transaction = BeginWriteTransaction(connection);

            var existing = await ReadEnvelopeAsync(
                    connection,
                    transaction,
                    envelope.Key,
                    cancellationToken)
                .ConfigureAwait(false);
            DurableInputEnqueueStatus status;
            if (existing is not null)
            {
                status = existing.HasSameContent(envelope)
                    ? DurableInputEnqueueStatus.AlreadyExists
                    : DurableInputEnqueueStatus.Conflict;
            }
            else
            {
                await InsertAsync(connection, transaction, envelope, cancellationToken)
                    .ConfigureAwait(false);
                status = DurableInputEnqueueStatus.Enqueued;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return new DurableInputEnqueueResult(envelope.Key, status);
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("enqueue", exception);
        }
    }

    public async ValueTask<IReadOnlyList<DurableInputLease>> LeaseAsync(
        DurableInputLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var transaction = BeginWriteTransaction(connection);
            var candidates = await ReadLeaseCandidatesAsync(
                    connection,
                    transaction,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            var leases = new List<DurableInputLease>(candidates.Count);

            foreach (var candidate in candidates)
            {
                var token = Guid.NewGuid();
                var affected = await ApplyLeaseAsync(
                        connection,
                        transaction,
                        candidate.Envelope.Key,
                        token,
                        request,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (affected != 1)
                {
                    throw new InvalidDataException(
                        $"SQL-file durable input lease candidate '{candidate.Envelope.Key}' changed inside an exclusive write transaction.");
                }

                leases.Add(new DurableInputLease(
                    candidate.Envelope,
                    token,
                    request.OwnerId,
                    request.Now,
                    request.LeaseUntil,
                    checked(candidate.Attempt + 1)));
            }

            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return leases;
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("lease", exception);
        }
    }

    public ValueTask<DurableInputTransitionResult> MarkDeliveredAsync(
        DurableInputLeaseTransition transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);
        return ApplyTransitionAsync(
            transition.Key,
            transition.LeaseToken,
            transition.OccurredAt,
            """
            UPDATE fluxflow_durable_inputs
            SET state = $newState,
                next_attempt_utc_ticks = NULL,
                lease_owner = NULL,
                lease_token = NULL,
                leased_at_utc_ticks = NULL,
                lease_until_utc_ticks = NULL,
                delivered_at_utc_ticks = $occurredAt,
                dead_lettered_at_utc_ticks = NULL
            WHERE application_address = $address
              AND message_id = $messageId
              AND state = $leasedState
              AND lease_token = $leaseToken
              AND lease_until_utc_ticks > $occurredAt;
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
                SET lease_until_utc_ticks = $leaseUntil
                WHERE application_address = $address
                  AND message_id = $messageId
                  AND state = $leasedState
                  AND lease_token = $leaseToken
                  AND lease_until_utc_ticks > $renewedAt;
                """;
            AddKey(command, renewal.Key);
            Add(command, "$leasedState", (int)DurableInputState.Leased);
            Add(
                command,
                "$leaseToken",
                renewal.LeaseToken.ToString("N", CultureInfo.InvariantCulture));
            Add(command, "$renewedAt", ToUtcTicks(renewal.RenewedAt));
            Add(command, "$leaseUntil", ToUtcTicks(renewal.LeaseUntil));

            var affected = await command.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
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
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("lease renewal", exception);
        }
    }

    public ValueTask<DurableInputTransitionResult> ReleaseAsync(
        DurableInputRelease release,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        return ApplyTransitionAsync(
            release.Key,
            release.LeaseToken,
            release.ReleasedAt,
            """
            UPDATE fluxflow_durable_inputs
            SET state = $newState,
                next_attempt_utc_ticks = $nextAttemptAt,
                lease_owner = NULL,
                lease_token = NULL,
                leased_at_utc_ticks = NULL,
                lease_until_utc_ticks = NULL,
                failure_kind = $failureKind,
                failure_description = $failureDescription,
                delivered_at_utc_ticks = NULL,
                dead_lettered_at_utc_ticks = NULL
            WHERE application_address = $address
              AND message_id = $messageId
              AND state = $leasedState
              AND lease_token = $leaseToken
              AND lease_until_utc_ticks > $occurredAt;
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
        return ApplyTransitionAsync(
            deadLetter.Key,
            deadLetter.LeaseToken,
            deadLetter.DeadLetteredAt,
            """
            UPDATE fluxflow_durable_inputs
            SET state = $newState,
                next_attempt_utc_ticks = NULL,
                lease_owner = NULL,
                lease_token = NULL,
                leased_at_utc_ticks = NULL,
                lease_until_utc_ticks = NULL,
                failure_kind = $failureKind,
                failure_description = $failureDescription,
                delivered_at_utc_ticks = NULL,
                dead_lettered_at_utc_ticks = $occurredAt,
                dead_letter_generation = dead_letter_generation + 1
            WHERE application_address = $address
              AND message_id = $messageId
              AND state = $leasedState
              AND lease_token = $leaseToken
              AND lease_until_utc_ticks > $occurredAt;
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
            using var connection = new SqliteConnection(_connectionString);
            SqliteConnection.ClearPool(connection);
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

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var transaction = BeginWriteTransaction(connection);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = commandText;
            AddKey(command, key);
            Add(command, "$leaseToken", leaseToken.ToString("N", CultureInfo.InvariantCulture));
            Add(command, "$occurredAt", ToUtcTicks(occurredAt));
            Add(command, "$newState", (int)newState);
            Add(command, "$leasedState", (int)DurableInputState.Leased);
            Add(command, "$nextAttemptAt", nextAttemptAt is null ? null : ToUtcTicks(nextAttemptAt.Value));
            Add(command, "$failureKind", failure is null ? null : (int)failure.Kind);
            Add(command, "$failureDescription", failure?.Description);

            var affected = await command.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
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
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("transition", exception);
        }
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

            PreparePath();
            try
            {
                await using var connection = await OpenConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);
                await SqlFileDurableInputSchema.InitializeAsync(connection, cancellationToken)
                    .ConfigureAwait(false);
                Volatile.Write(ref _initialized, 1);
            }
            catch (SqliteException exception) when (IsBusy(exception))
            {
                throw CreateBusyException("schema initialization", exception);
            }
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private void PreparePath()
    {
        var directory = Path.GetDirectoryName(_settings.DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            if (!_settings.CreateDirectory)
            {
                throw new DirectoryNotFoundException(
                    $"SQL-file durable input directory '{directory}' does not exist.");
            }

            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(_settings.DatabasePath) && !_settings.CreateDatabase)
        {
            throw new FileNotFoundException(
                $"SQL-file durable input database '{_settings.DatabasePath}' does not exist.",
                _settings.DatabasePath);
        }
    }

    private async ValueTask<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var connection = new SqliteConnection(_connectionString);
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

    private static SqliteTransaction BeginWriteTransaction(SqliteConnection connection)
        => connection.BeginTransaction(deferred: false);

    private static async ValueTask InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DurableInputEnvelope envelope,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO fluxflow_durable_inputs (
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
                next_attempt_utc_ticks
            )
            VALUES (
                $address,
                $messageId,
                $contractName,
                $schemaVersion,
                $isError,
                $payloadJson,
                $errorCode,
                $errorMessage,
                $errorCategory,
                $errorIsTransient,
                $errorDetailsJson,
                $traceId,
                $correlationId,
                $causationId,
                $messageTimestamp,
                $messageTimestampOffset,
                $enqueuedAt,
                $enqueuedAtOffset,
                $headersJson,
                $state,
                0,
                $nextAttemptAt
            );
            """;
        AddEnvelopeParameters(command, envelope);
        Add(command, "$state", (int)DurableInputState.Pending);
        Add(command, "$nextAttemptAt", ToUtcTicks(envelope.EnqueuedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<DurableInputEnvelope?> ReadEnvelopeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DurableInputKey key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {EnvelopeColumns}
            FROM fluxflow_durable_inputs
            WHERE application_address = $address
              AND message_id = $messageId;
            """;
        AddKey(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadEnvelope(reader)
            : null;
    }

    private static async ValueTask<IReadOnlyList<LeaseCandidate>> ReadLeaseCandidatesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DurableInputLeaseRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {EnvelopeColumns}, attempt
            FROM fluxflow_durable_inputs
            WHERE (state = $pendingState AND next_attempt_utc_ticks <= $now)
               OR (state = $leasedState AND lease_until_utc_ticks <= $now)
            ORDER BY CASE state
                         WHEN $pendingState THEN next_attempt_utc_ticks
                         ELSE lease_until_utc_ticks
                     END,
                     enqueued_at_utc_ticks,
                     application_address COLLATE BINARY,
                     message_id COLLATE BINARY
            LIMIT $maxCount;
            """;
        Add(command, "$pendingState", (int)DurableInputState.Pending);
        Add(command, "$leasedState", (int)DurableInputState.Leased);
        Add(command, "$now", ToUtcTicks(request.Now));
        Add(command, "$maxCount", request.MaxCount);

        var candidates = new List<LeaseCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var envelope = ReadEnvelope(reader);
            var attempt = reader.GetInt32(EnvelopeColumnCount);
            if (attempt < 0)
                throw Corrupt(envelope.Key, "attempt cannot be negative");
            candidates.Add(new LeaseCandidate(envelope, attempt));
        }

        return candidates;
    }

    private static async ValueTask<int> ApplyLeaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DurableInputKey key,
        Guid leaseToken,
        DurableInputLeaseRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE fluxflow_durable_inputs
            SET state = $leasedState,
                attempt = attempt + 1,
                next_attempt_utc_ticks = NULL,
                lease_owner = $leaseOwner,
                lease_token = $leaseToken,
                leased_at_utc_ticks = $leasedAt,
                lease_until_utc_ticks = $leaseUntil,
                delivered_at_utc_ticks = NULL,
                dead_lettered_at_utc_ticks = NULL
            WHERE application_address = $address
              AND message_id = $messageId
              AND ((state = $pendingState AND next_attempt_utc_ticks <= $now)
                   OR (state = $leasedState AND lease_until_utc_ticks <= $now));
            """;
        AddKey(command, key);
        Add(command, "$pendingState", (int)DurableInputState.Pending);
        Add(command, "$leasedState", (int)DurableInputState.Leased);
        Add(command, "$now", ToUtcTicks(request.Now));
        Add(command, "$leaseOwner", request.OwnerId);
        Add(command, "$leaseToken", leaseToken.ToString("N", CultureInfo.InvariantCulture));
        Add(command, "$leasedAt", ToUtcTicks(request.Now));
        Add(command, "$leaseUntil", ToUtcTicks(request.LeaseUntil));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<DurableInputTransitionStatus> ResolveTransitionStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DurableInputKey key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT state
            FROM fluxflow_durable_inputs
            WHERE application_address = $address
              AND message_id = $messageId;
            """;
        AddKey(command, key);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null || value is DBNull)
            return DurableInputTransitionStatus.NotFound;

        var stateValue = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        if (!Enum.IsDefined(typeof(DurableInputState), stateValue))
            throw Corrupt(key, $"state value {stateValue} is invalid");

        return (DurableInputState)stateValue == DurableInputState.Leased
            ? DurableInputTransitionStatus.LeaseLost
            : DurableInputTransitionStatus.InvalidState;
    }

    private static DurableInputEnvelope ReadEnvelope(SqliteDataReader reader)
    {
        DurableInputKey? key = null;
        try
        {
            var address = ApplicationAddress.Parse(reader.GetString(0));
            var messageId = new MessageId(reader.GetString(1));
            key = new DurableInputKey(address, messageId);
            var contractName = reader.GetString(2);
            var schemaVersion = reader.GetInt32(3);
            var isErrorValue = reader.GetInt32(4);
            if (isErrorValue is not (0 or 1))
                throw Corrupt(key.Value, $"is_error value {isErrorValue} is invalid");

            var payload = ParseJson(reader.GetString(5), key.Value, "payload");
            FlowError? error = null;
            if (isErrorValue == 1)
            {
                if (reader.IsDBNull(6) || reader.IsDBNull(7) ||
                    reader.IsDBNull(8) || reader.IsDBNull(9))
                {
                    throw Corrupt(key.Value, "error fields are incomplete");
                }

                var transientValue = reader.GetInt32(9);
                if (transientValue is not (0 or 1))
                    throw Corrupt(key.Value, $"error transient value {transientValue} is invalid");
                error = new FlowError(
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    transientValue == 1,
                    reader.IsDBNull(10)
                        ? null
                        : ParseJson(reader.GetString(10), key.Value, "error details"));
            }

            var traceId = new TraceId(reader.GetString(11));
            var correlationId = reader.IsDBNull(12)
                ? (CorrelationId?)null
                : new CorrelationId(reader.GetString(12));
            var causationId = reader.IsDBNull(13)
                ? (MessageId?)null
                : new MessageId(reader.GetString(13));
            var timestamp = FromStoredTime(
                reader.GetInt64(14),
                reader.GetInt32(15),
                key.Value,
                "message timestamp");
            var enqueuedAt = FromStoredTime(
                reader.GetInt64(16),
                reader.GetInt32(17),
                key.Value,
                "enqueue timestamp");
            var headers = ReadHeaders(reader.GetString(18), key.Value);

            return new DurableInputEnvelope(
                address,
                contractName,
                isErrorValue == 1,
                payload,
                error,
                messageId,
                traceId,
                timestamp,
                enqueuedAt,
                correlationId,
                causationId,
                headers,
                schemaVersion);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            ArgumentException or FormatException or InvalidOperationException or
            JsonException or OverflowException)
        {
            throw new InvalidDataException(
                key is null
                    ? "SQL-file durable input row contains an invalid key."
                    : $"SQL-file durable input row '{key}' is corrupt.",
                exception);
        }
    }

    private static Dictionary<string, string> ReadHeaders(
        string json,
        DurableInputKey key)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, SerializerOptions)
                   ?? throw Corrupt(key, "headers JSON cannot be null");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"SQL-file durable input row '{key}' contains invalid headers JSON.",
                exception);
        }
    }

    private static JsonElement ParseJson(
        string json,
        DurableInputKey key,
        string field)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"SQL-file durable input row '{key}' contains invalid {field} JSON.",
                exception);
        }
    }

    private static DateTimeOffset FromStoredTime(
        long utcTicks,
        int offsetMinutes,
        DurableInputKey key,
        string field)
    {
        try
        {
            var offset = TimeSpan.FromMinutes(offsetMinutes);
            return new DateTimeOffset(utcTicks, TimeSpan.Zero).ToOffset(offset);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException(
                $"SQL-file durable input row '{key}' contains an invalid {field}.",
                exception);
        }
    }

    private static void AddEnvelopeParameters(
        SqliteCommand command,
        DurableInputEnvelope envelope)
    {
        AddKey(command, envelope.Key);
        Add(command, "$contractName", envelope.ContractName);
        Add(command, "$schemaVersion", envelope.SchemaVersion);
        Add(command, "$isError", envelope.IsError ? 1 : 0);
        Add(command, "$payloadJson", envelope.Payload.GetRawText());
        Add(command, "$errorCode", envelope.Error?.Code);
        Add(command, "$errorMessage", envelope.Error?.Message);
        Add(command, "$errorCategory", envelope.Error?.Category);
        Add(command, "$errorIsTransient", envelope.Error is null ? null : envelope.Error.IsTransient ? 1 : 0);
        Add(command, "$errorDetailsJson", envelope.Error?.Details?.GetRawText());
        Add(command, "$traceId", envelope.TraceId.Value);
        Add(command, "$correlationId", envelope.CorrelationId?.Value);
        Add(command, "$causationId", envelope.CausationId?.Value);
        Add(command, "$messageTimestamp", ToUtcTicks(envelope.Timestamp));
        Add(command, "$messageTimestampOffset", ToOffsetMinutes(envelope.Timestamp));
        Add(command, "$enqueuedAt", ToUtcTicks(envelope.EnqueuedAt));
        Add(command, "$enqueuedAtOffset", ToOffsetMinutes(envelope.EnqueuedAt));
        Add(
            command,
            "$headersJson",
            JsonSerializer.Serialize(envelope.Headers, SerializerOptions));
    }

    private static void AddKey(SqliteCommand command, DurableInputKey key)
    {
        Add(command, "$address", key.Address.Value);
        Add(command, "$messageId", key.MessageId.Value);
    }

    private static void Add(SqliteCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static long ToUtcTicks(DateTimeOffset value) => value.UtcTicks;

    private static int ToOffsetMinutes(DateTimeOffset value)
        => checked((int)value.Offset.TotalMinutes);

    private static string CreateConnectionString(SqlFileDurableInputStoreSettings settings)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = settings.DatabasePath,
            Mode = settings.CreateDatabase
                ? SqliteOpenMode.ReadWriteCreate
                : SqliteOpenMode.ReadWrite,
            Pooling = true,
            DefaultTimeout = Math.Max(1, (int)Math.Ceiling(settings.BusyTimeout.TotalSeconds))
        };
        return builder.ToString();
    }

    private InvalidOperationException CreateBusyException(
        string operation,
        SqliteException exception)
        => new(
            $"SQL-file durable input {operation} could not acquire the database within the configured busy timeout of {_settings.BusyTimeout}.",
            exception);

    private static bool IsBusy(SqliteException exception)
        => exception.SqliteErrorCode is SqliteBusy or SqliteLocked;

    private static InvalidDataException Corrupt(DurableInputKey key, string detail)
        => new($"SQL-file durable input row '{key}' is corrupt: {detail}.");

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
    }

    private const string EnvelopeColumns = """
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
        headers_json
        """;

    private const int EnvelopeColumnCount = 19;

    private sealed record LeaseCandidate(DurableInputEnvelope Envelope, int Attempt);
}
