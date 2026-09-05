using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Data.Sqlite;

namespace FluxFlow.Engine.DurableOutput.SqlFile;

/// <summary>
/// SQLite single-file implementation of the durable-output store contract.
/// </summary>
public sealed partial class SqlFileDurableOutputStore :
    IDurableOutputStore,
    IDurableOutputDeliveryStore,
    IDurableOutputDeadLetterStore,
    IDurableOutputStatusStore,
    IDurableOutputRetentionStore,
    IAsyncDisposable
{
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;

    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SqlFileDurableOutputStoreSettings _settings;
    private readonly string _connectionString;
    private int _initialized;
    private int _disposed;

    public SqlFileDurableOutputStore(SqlFileDurableOutputStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _settings = options.Resolve();
        _connectionString = CreateConnectionString(_settings);
    }

    public async ValueTask<DurableOutputEnqueueResult> EnqueueAsync(
        DurableOutputEnvelope envelope,
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
            DurableOutputEnqueueStatus status;
            if (existing is null)
            {
                await InsertAsync(connection, transaction, envelope, cancellationToken)
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
        catch (SqliteException exception) when (IsBusy(exception))
        {
            throw CreateBusyException("enqueue", exception);
        }
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
                await SqlFileDurableOutputSchema.InitializeAsync(connection, cancellationToken)
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
                    $"SQL-file durable output directory '{directory}' does not exist.");
            }

            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(_settings.DatabasePath) && !_settings.CreateDatabase)
        {
            throw new FileNotFoundException(
                $"SQL-file durable output database '{_settings.DatabasePath}' does not exist.",
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
                $"PRAGMA busy_timeout = {_settings.BusyTimeoutMilliseconds}; PRAGMA foreign_keys = ON;");
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
        DurableOutputEnvelope envelope,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO fluxflow_durable_outputs (
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
                captured_at_utc_ticks,
                captured_at_offset_minutes,
                headers_json
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
                $capturedAt,
                $capturedAtOffset,
                $headersJson
            );
            """;
        AddEnvelopeParameters(command, envelope);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
            throw new InvalidDataException("SQL-file durable output enqueue did not insert exactly one row.");
    }

    private static async ValueTask<DurableOutputEnvelope?> ReadEnvelopeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DurableOutputKey key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {EnvelopeColumns}
            FROM fluxflow_durable_outputs
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

    private static DurableOutputEnvelope ReadEnvelope(SqliteDataReader reader)
    {
        DurableOutputKey? key = null;
        try
        {
            var address = ApplicationAddress.Parse(reader.GetString(0));
            var messageId = new MessageId(reader.GetString(1));
            key = new DurableOutputKey(address, messageId);
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
            else if (!reader.IsDBNull(6) || !reader.IsDBNull(7) ||
                     !reader.IsDBNull(8) || !reader.IsDBNull(9) || !reader.IsDBNull(10))
            {
                throw Corrupt(key.Value, "value row contains error fields");
            }

            var traceId = new TraceId(reader.GetString(11));
            var correlationId = reader.IsDBNull(12)
                ? (CorrelationId?)null
                : new CorrelationId(reader.GetString(12));
            if (correlationId is { IsEmpty: true })
                throw Corrupt(key.Value, "correlation id cannot be empty");
            var causationId = reader.IsDBNull(13)
                ? (MessageId?)null
                : new MessageId(reader.GetString(13));
            if (causationId is { IsEmpty: true })
                throw Corrupt(key.Value, "causation id cannot be empty");

            var timestamp = FromStoredTime(
                reader.GetInt64(14),
                reader.GetInt32(15),
                key.Value,
                "message timestamp");
            var capturedAt = FromStoredTime(
                reader.GetInt64(16),
                reader.GetInt32(17),
                key.Value,
                "capture timestamp");
            var headers = ReadHeaders(reader.GetString(18), key.Value);

            return new DurableOutputEnvelope(
                address,
                contractName,
                isErrorValue == 1,
                payload,
                error,
                messageId,
                traceId,
                timestamp,
                capturedAt,
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
                    ? "SQL-file durable output row contains an invalid key."
                    : $"SQL-file durable output row '{key}' is corrupt.",
                exception);
        }
    }

    private static Dictionary<string, string> ReadHeaders(
        string json,
        DurableOutputKey key)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw Corrupt(key, "headers JSON must be an object");

            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    throw Corrupt(key, $"header '{property.Name}' must contain a string value");
                headers.Add(property.Name, property.Value.GetString()!);
            }

            return headers;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException(
                $"SQL-file durable output row '{key}' contains invalid headers JSON.",
                exception);
        }
    }

    private static JsonElement ParseJson(
        string json,
        DurableOutputKey key,
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
                $"SQL-file durable output row '{key}' contains invalid {field} JSON.",
                exception);
        }
    }

    private static DateTimeOffset FromStoredTime(
        long utcTicks,
        int offsetMinutes,
        DurableOutputKey key,
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
                $"SQL-file durable output row '{key}' contains an invalid {field}.",
                exception);
        }
    }

    private static void AddEnvelopeParameters(
        SqliteCommand command,
        DurableOutputEnvelope envelope)
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
        Add(command, "$capturedAt", ToUtcTicks(envelope.CapturedAt));
        Add(command, "$capturedAtOffset", ToOffsetMinutes(envelope.CapturedAt));
        Add(command, "$headersJson", SerializeHeaders(envelope.Headers));
    }

    private static string SerializeHeaders(IReadOnlyDictionary<string, string> headers)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var header in headers.OrderBy(static item => item.Key, StringComparer.Ordinal))
                writer.WriteString(header.Key, header.Value);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void AddKey(SqliteCommand command, DurableOutputKey key)
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

    private static string CreateConnectionString(SqlFileDurableOutputStoreSettings settings)
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
            $"SQL-file durable output {operation} could not acquire the database within the configured busy timeout of {_settings.BusyTimeout}.",
            exception);

    private static bool IsBusy(SqliteException exception)
        => exception.SqliteErrorCode is SqliteBusy or SqliteLocked;

    private static InvalidDataException Corrupt(DurableOutputKey key, string detail)
        => new($"SQL-file durable output row '{key}' is corrupt: {detail}.");

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
        captured_at_utc_ticks,
        captured_at_offset_minutes,
        headers_json
        """;
}
