using FluxFlow.Components.Storage.Contracts;
using Microsoft.Data.Sqlite;
using System.Text;
using System.Text.Json;

namespace FluxFlow.Components.Storage.SqlFile;

public sealed class SqlFileStorageStore : IStorageStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SqlFileStorageStoreSettings _settings;
    private bool _disposed;

    public SqlFileStorageStore(SqlFileStorageStoreOptions options)
        : this(options, context: null)
    {
    }

    public SqlFileStorageStore(
        SqlFileStorageStoreOptions options,
        StorageStoreContext? context)
    {
        ArgumentNullException.ThrowIfNull(options);
        _settings = options.Resolve(context);
        InitializeDatabase();
    }

    public async Task<StorageRecord> PutAsync(
        StoragePutRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var collection = ResolveCollection(request.Collection);
        var key = ResolveKey(request.Key);
        var mode = ResolveWriteMode(request.Mode);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            ApplyBusyTimeout(connection);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var existing = await ReadRecordAsync(
                    connection,
                    transaction,
                    collection,
                    key,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing?.ExpiresAt is { } expiresAt && expiresAt <= _settings.Clock.GetUtcNow())
            {
                existing = null;
            }

            if (mode == StorageWriteMode.Create && existing is not null)
            {
                throw new InvalidOperationException("SQL file storage record already exists.");
            }

            if (mode == StorageWriteMode.Replace && existing is null)
            {
                throw new InvalidOperationException("SQL file storage record does not exist.");
            }

            if (request.ExpectedVersion.HasValue &&
                (existing?.Version ?? 0) != request.ExpectedVersion.Value)
            {
                throw new InvalidOperationException("SQL file storage record version did not match.");
            }

            var storedValue = CreateStoredValue(request.Value);
            var attributes = CopyAttributes(request.Attributes);
            var storedAt = TruncateToMilliseconds(_settings.Clock.GetUtcNow());
            var record = new StorageRecord
            {
                Collection = collection,
                Key = key,
                Value = request.Value,
                ContentType = Normalize(request.ContentType),
                Attributes = attributes,
                Version = (existing?.Version ?? 0) + 1,
                StoredAt = storedAt,
                ExpiresAt = TruncateToMilliseconds(request.ExpiresAt),
                CorrelationId = Normalize(request.CorrelationId)
            };

            await UpsertRecordAsync(
                    connection,
                    transaction,
                    record,
                    storedValue,
                    cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return CopyRecord(record);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StorageRecord?> GetAsync(
        StorageGetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var collection = ResolveCollection(request.Collection);
        var key = ResolveKey(request.Key);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            ApplyBusyTimeout(connection);
            var record = await ReadRecordAsync(
                    connection,
                    transaction: null,
                    collection,
                    key,
                    cancellationToken)
                .ConfigureAwait(false);

            if (record is null || IsExpired(record, request.IncludeExpired))
            {
                return null;
            }

            return CopyRecord(record);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<StorageRecord>> QueryAsync(
        StorageQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var collection = ResolveCollection(request.Collection);
        var query = request with
        {
            Collection = collection,
            KeyPrefix = Normalize(request.KeyPrefix),
            Attributes = CopyAttributes(request.Attributes),
            Offset = request.Offset ?? 0,
            Limit = request.Limit ?? int.MaxValue
        };
        StorageQueryMatcher.Validate(query);
        var limit = query.Limit!.Value;
        var offset = query.Offset!.Value;
        var pagingPushedDown = query.Attributes.Count == 0;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            ApplyBusyTimeout(connection);
            var now = _settings.Clock.GetUtcNow();

            var records = await ReadCollectionAsync(
                    connection,
                    collection,
                    query,
                    pagingPushedDown,
                    now,
                    cancellationToken)
                .ConfigureAwait(false);

            var matches = records
                .Where(record => StorageQueryMatcher.IsMatch(record, query, now))
                .OrderBy(record => record.StoredAt)
                .ThenBy(record => record.Key, StringComparer.Ordinal);
            var page = pagingPushedDown
                ? matches
                : matches.Skip(offset).Take(limit);
            return page
                .Select(CopyRecord)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StorageResult> DeleteAsync(
        StorageDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var collection = ResolveCollection(request.Collection);
        var key = ResolveKey(request.Key);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            ApplyBusyTimeout(connection);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            var record = await ReadRecordAsync(
                    connection,
                    transaction,
                    collection,
                    key,
                    cancellationToken)
                .ConfigureAwait(false);

            if (record is not null)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    DELETE FROM storage_records
                    WHERE store_name = $storeName
                      AND collection = $collection
                      AND record_key = $key;
                    """;
                Add(command, "$storeName", _settings.StoreName);
                Add(command, "$collection", collection);
                Add(command, "$key", key);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new StorageResult
            {
                Timestamp = _settings.Clock.GetUtcNow(),
                Operation = "delete",
                Collection = collection,
                Key = key,
                Succeeded = true,
                Found = record is not null,
                Deleted = record is not null,
                Record = record is null ? null : CopyRecord(record),
                Version = record?.Version,
                CorrelationId = Normalize(request.CorrelationId),
                Attributes = record is null ? [] : CopyAttributes(record.Attributes)
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
        }
        finally
        {
            _gate.Release();
        }

        SqliteConnection.ClearPool(new SqliteConnection(CreateConnectionString()));
    }

    private void InitializeDatabase()
    {
        using var connection = CreateConnection();
        connection.Open();
        ApplyBusyTimeout(connection);
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS storage_records (
                store_name TEXT NOT NULL,
                collection TEXT NOT NULL,
                record_key TEXT NOT NULL,
                value_json TEXT NULL,
                value_bytes INTEGER NOT NULL,
                content_type TEXT NULL,
                attributes_json TEXT NOT NULL,
                version INTEGER NOT NULL,
                stored_at_ms INTEGER NOT NULL,
                expires_at_ms INTEGER NULL,
                correlation_id TEXT NULL,
                PRIMARY KEY (store_name, collection, record_key)
            );

            CREATE INDEX IF NOT EXISTS ix_storage_records_query
                ON storage_records (store_name, collection, stored_at_ms, record_key);

            CREATE INDEX IF NOT EXISTS ix_storage_records_expires
                ON storage_records (store_name, collection, expires_at_ms);
            """;
        command.ExecuteNonQuery();
    }

    private async Task UpsertRecordAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        StorageRecord record,
        StoredJsonValue? storedValue,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO storage_records (
                store_name,
                collection,
                record_key,
                value_json,
                value_bytes,
                content_type,
                attributes_json,
                version,
                stored_at_ms,
                expires_at_ms,
                correlation_id
            )
            VALUES (
                $storeName,
                $collection,
                $key,
                $valueJson,
                $valueBytes,
                $contentType,
                $attributesJson,
                $version,
                $storedAtMs,
                $expiresAtMs,
                $correlationId
            )
            ON CONFLICT(store_name, collection, record_key) DO UPDATE SET
                value_json = excluded.value_json,
                value_bytes = excluded.value_bytes,
                content_type = excluded.content_type,
                attributes_json = excluded.attributes_json,
                version = excluded.version,
                stored_at_ms = excluded.stored_at_ms,
                expires_at_ms = excluded.expires_at_ms,
                correlation_id = excluded.correlation_id;
            """;
        AddRecordParameters(command, record, storedValue);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<StorageRecord?> ReadRecordAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string collection,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        if (transaction is not null)
        {
            command.Transaction = (SqliteTransaction)transaction;
        }

        command.CommandText = """
            SELECT collection,
                   record_key,
                   value_json,
                   content_type,
                   attributes_json,
                   version,
                   stored_at_ms,
                   expires_at_ms,
                   correlation_id
            FROM storage_records
            WHERE store_name = $storeName
              AND collection = $collection
              AND record_key = $key;
            """;
        Add(command, "$storeName", _settings.StoreName);
        Add(command, "$collection", collection);
        Add(command, "$key", key);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadRecord(reader);
    }

    private async Task<IReadOnlyList<StorageRecord>> ReadCollectionAsync(
        SqliteConnection connection,
        string collection,
        StorageQueryRequest request,
        bool pagingPushedDown,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var text = new StringBuilder("""
            SELECT collection,
                   record_key,
                   value_json,
                   content_type,
                   attributes_json,
                   version,
                   stored_at_ms,
                   expires_at_ms,
                   correlation_id
            FROM storage_records
            WHERE store_name = $storeName
              AND collection = $collection
            """);
        text.AppendLine();

        Add(command, "$storeName", _settings.StoreName);
        Add(command, "$collection", collection);

        if (request.KeyPrefix is not null)
        {
            ApplyCaseSensitiveLike(connection);
            text.AppendLine(@"  AND record_key LIKE $keyPrefix || '%' ESCAPE '\'");
            Add(command, "$keyPrefix", EscapeLikePrefix(request.KeyPrefix));
        }

        if (request.StoredFrom.HasValue)
        {
            text.AppendLine("  AND stored_at_ms >= $storedFromMs");
            Add(command, "$storedFromMs", ToCeilingUnixTimeMilliseconds(request.StoredFrom.Value));
        }

        if (request.StoredTo.HasValue)
        {
            text.AppendLine("  AND stored_at_ms <= $storedToMs");
            Add(command, "$storedToMs", request.StoredTo.Value.ToUnixTimeMilliseconds());
        }

        if (request.IncludeExpired != true)
        {
            text.AppendLine("  AND (expires_at_ms IS NULL OR expires_at_ms > $nowMs)");
            Add(command, "$nowMs", now.ToUnixTimeMilliseconds());
        }

        text.AppendLine("ORDER BY stored_at_ms, record_key");
        if (pagingPushedDown)
        {
            text.AppendLine("LIMIT $limit OFFSET $offset");
            Add(command, "$limit", request.Limit!.Value);
            Add(command, "$offset", request.Offset!.Value);
        }

        text.Append(';');
        command.CommandText = text.ToString();

        var records = new List<StorageRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            records.Add(ReadRecord(reader));
        }

        return records;
    }

    private StorageRecord ReadRecord(SqliteDataReader reader)
    {
        var valueJson = reader.IsDBNull(2) ? null : reader.GetString(2);
        var attributesJson = reader.GetString(4);
        var expiresAt = reader.IsDBNull(7)
            ? (DateTimeOffset?)null
            : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7));

        return new StorageRecord
        {
            Collection = reader.GetString(0),
            Key = reader.GetString(1),
            Value = ConvertValue(valueJson),
            ContentType = reader.IsDBNull(3) ? null : reader.GetString(3),
            Attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    attributesJson,
                    SerializerOptions) ??
                [],
            Version = reader.GetInt64(5),
            StoredAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6)),
            ExpiresAt = expiresAt,
            CorrelationId = reader.IsDBNull(8) ? null : reader.GetString(8)
        };
    }

    private StoredJsonValue? CreateStoredValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var element = JsonSerializer.SerializeToElement(value, SerializerOptions);
        var raw = element.GetRawText();
        var valueBytes = Encoding.UTF8.GetByteCount(raw);
        if (valueBytes > _settings.MaxValueBytes)
        {
            throw new InvalidOperationException(
                $"SQL file storage value exceeds {_settings.MaxValueBytes} bytes.");
        }

        return new StoredJsonValue(raw, valueBytes);
    }

    private void AddRecordParameters(
        SqliteCommand command,
        StorageRecord record,
        StoredJsonValue? storedValue)
    {
        Add(command, "$storeName", _settings.StoreName);
        Add(command, "$collection", record.Collection);
        Add(command, "$key", record.Key);
        Add(command, "$valueJson", storedValue?.Json);
        Add(command, "$valueBytes", storedValue?.ByteCount ?? 0);
        Add(command, "$contentType", record.ContentType);
        Add(command, "$attributesJson", JsonSerializer.Serialize(record.Attributes, SerializerOptions));
        Add(command, "$version", record.Version);
        Add(command, "$storedAtMs", record.StoredAt.ToUnixTimeMilliseconds());
        Add(command, "$expiresAtMs", record.ExpiresAt?.ToUnixTimeMilliseconds());
        Add(command, "$correlationId", record.CorrelationId);
    }

    private string ResolveCollection(string? value)
    {
        var collection = Normalize(value) ?? _settings.DefaultCollection;
        if (collection is null)
        {
            throw new InvalidOperationException(
                "SQL file storage request requires a collection.");
        }

        return collection;
    }

    private static string ResolveKey(string? value)
    {
        var key = Normalize(value);
        if (key is null)
        {
            throw new InvalidOperationException(
                "SQL file storage request requires a key.");
        }

        return key;
    }

    private static StorageWriteMode ResolveWriteMode(StorageWriteMode? value)
    {
        var mode = value ?? StorageWriteMode.Upsert;
        if (!Enum.IsDefined(mode))
        {
            throw new InvalidOperationException(
                $"SQL file storage write mode '{mode}' is not supported.");
        }

        return mode;
    }

    private bool IsExpired(StorageRecord record, bool? includeExpired)
        => record.ExpiresAt.HasValue &&
            record.ExpiresAt.Value <= _settings.Clock.GetUtcNow() &&
            includeExpired != true;

    private SqliteConnection CreateConnection()
        => new(CreateConnectionString());

    private string CreateConnectionString()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _settings.DatabasePath,
            Mode = _settings.CreateDatabase
                ? SqliteOpenMode.ReadWriteCreate
                : SqliteOpenMode.ReadWrite
        };

        return builder.ToString();
    }

    private void ApplyBusyTimeout(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA busy_timeout = {_settings.BusyTimeoutMilliseconds};";
        command.ExecuteNonQuery();
    }

    private static void ApplyCaseSensitiveLike(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA case_sensitive_like = ON;";
        command.ExecuteNonQuery();
    }

    private static string EscapeLikePrefix(string prefix)
    {
        var builder = new StringBuilder(prefix.Length);
        foreach (var character in prefix)
        {
            if (character is '%' or '_' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static DateTimeOffset TruncateToMilliseconds(DateTimeOffset value)
        => DateTimeOffset.FromUnixTimeMilliseconds(value.ToUnixTimeMilliseconds());

    private static DateTimeOffset? TruncateToMilliseconds(DateTimeOffset? value)
        => value.HasValue ? TruncateToMilliseconds(value.Value) : null;

    private static long ToCeilingUnixTimeMilliseconds(DateTimeOffset value)
    {
        var milliseconds = value.ToUnixTimeMilliseconds();
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) < value
            ? milliseconds + 1
            : milliseconds;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SqlFileStorageStore));
        }
    }

    private static void Add(
        SqliteCommand command,
        string name,
        object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static StorageRecord CopyRecord(StorageRecord record)
        => record with
        {
            Value = CopyValue(record.Value),
            Attributes = CopyAttributes(record.Attributes)
        };

    private static object? CopyValue(object? value)
        => value is JsonElement element ? element.Clone() : value;

    private static Dictionary<string, string> CopyAttributes(
        Dictionary<string, string>? source)
    {
        if (source is null)
        {
            return [];
        }

        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in source)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("SQL file storage attribute keys are required.");
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("SQL file storage attribute values are required.");
            }

            var normalizedKey = key.Trim();
            if (!copy.TryAdd(normalizedKey, value.Trim()))
            {
                throw new InvalidOperationException(
                    $"SQL file storage attribute '{normalizedKey}' is declared more than once.");
            }
        }

        return copy;
    }

    private static object? ConvertValue(string? valueJson)
    {
        if (valueJson is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(valueJson);
        var element = document.RootElement;
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var number) => number,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => element.Clone()
        };
    }

    private static string? Normalize(string? value)
        => SqlFileStorageStoreOptions.Normalize(value);

    private sealed record StoredJsonValue(string Json, long ByteCount);
}
