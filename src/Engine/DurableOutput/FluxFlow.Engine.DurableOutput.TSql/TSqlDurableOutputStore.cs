using System.Data;
using Microsoft.Data.SqlClient;

namespace FluxFlow.Engine.DurableOutput.TSql;

public sealed partial class TSqlDurableOutputStore :
    IDurableOutputStore,
    IDurableOutputDeliveryStore,
    IDurableOutputDeadLetterStore,
    IDurableOutputStatusStore,
    IDurableOutputRetentionStore,
    IAsyncDisposable
{
    private const int DeliveryPending = 1;
    private const int DeliveryLeased = 2;
    private const int DeliveryCompleted = 3;
    private const int DeliveryDeadLettered = 4;

    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly TSqlDurableOutputStoreSettings _options;
    private int _initialized;
    private int _disposed;

    public TSqlDurableOutputStore(TSqlDurableOutputStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Resolve();
    }

    public async ValueTask<DurableOutputEnqueueResult> EnqueueAsync(
        DurableOutputEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        RelationalDurableOutputRows.ValidateEnvelope(envelope);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
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

        DurableOutputEnqueueStatus status;
        if (existing is null)
        {
            await InsertEnvelopeAsync(connection, transaction, envelope, cancellationToken)
                .ConfigureAwait(false);
            status = DurableOutputEnqueueStatus.Enqueued;
        }
        else
        {
            status = existing.HasSameContent(envelope)
                ? DurableOutputEnqueueStatus.AlreadyExists
                : DurableOutputEnqueueStatus.Conflict;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return new DurableOutputEnqueueResult(envelope.Key, status);
    }

    internal async ValueTask<DurableOutputEnvelope?> ReadAsync(
        DurableOutputKey key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadEnvelopeAsync(connection, transaction: null, key, lockKey: false, cancellationToken)
            .ConfigureAwait(false);
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

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await RelationalDurableOutputSchema.InitializeAsync(connection, _options, cancellationToken)
                .ConfigureAwait(false);
            Volatile.Write(ref _initialized, 1);
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async ValueTask<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var connection = new SqlConnection(_options.NormalizedConnectionString);
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

    private async ValueTask InsertEnvelopeAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DurableOutputEnvelope envelope,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction);
        command.CommandText = """
            INSERT INTO dbo.fluxflow_relational_outputs (
                application_address, message_id, contract_name, envelope_schema_version,
                is_error, payload_json, error_code, error_message, error_category,
                error_is_transient, error_details_json, trace_id, correlation_id,
                causation_id, message_timestamp_utc_ticks, message_timestamp_offset_minutes,
                captured_at_utc_ticks, captured_at_offset_minutes, headers_json)
            VALUES (
                @address, @messageId, @contractName, @schemaVersion,
                @isError, @payloadJson, @errorCode, @errorMessage, @errorCategory,
                @errorIsTransient, @errorDetailsJson, @traceId, @correlationId,
                @causationId, @messageTimestamp, @messageTimestampOffset,
                @capturedAt, @capturedAtOffset, @headersJson);
            """;
        RelationalDurableOutputRows.AddEnvelopeParameters(command, envelope);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
            throw new InvalidDataException("Relational durable-output enqueue did not insert exactly one row.");
    }

    private async ValueTask<DurableOutputEnvelope?> ReadEnvelopeAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        DurableOutputKey key,
        bool lockKey,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction);
        var hints = lockKey ? "WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        command.CommandText = $"""
            SELECT {RelationalDurableOutputRows.EnvelopeColumns}
            FROM dbo.fluxflow_relational_outputs AS o {hints}
            WHERE o.application_address = @address COLLATE Latin1_General_100_BIN2
              AND o.message_id = @messageId COLLATE Latin1_General_100_BIN2;
            """;
        RelationalDurableOutputRows.AddKey(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? RelationalDurableOutputRows.ReadEnvelope(reader)
            : null;
    }

    private SqlCommand CreateCommand(
        SqlConnection connection,
        SqlTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        return command;
    }

    private static void ValidateKey(DurableOutputKey key)
        => RelationalDurableOutputRows.ValidateKey(key);

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
