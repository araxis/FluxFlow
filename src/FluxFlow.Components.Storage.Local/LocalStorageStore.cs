using FluxFlow.Components.Storage.Contracts;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FluxFlow.Components.Storage.Local;

public sealed class LocalStorageStore : IStorageStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly LocalStorageStoreSettings _settings;

    public LocalStorageStore(LocalStorageStoreOptions options)
        : this(options, context: null)
    {
    }

    public LocalStorageStore(
        LocalStorageStoreOptions options,
        StorageStoreContext? context)
    {
        ArgumentNullException.ThrowIfNull(options);
        _settings = options.Resolve(context);
    }

    public Task<StorageRecord> PutAsync(
        StoragePutRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var collection = ResolveCollection(request.Collection);
        var key = ResolveKey(request.Key);
        var path = GetRecordPath(collection, key);
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = ReadRecord(path, collection, key);
            var mode = request.Mode ?? StorageWriteMode.Upsert;
            if (mode == StorageWriteMode.Create && existing is not null)
            {
                throw new InvalidOperationException("Local storage record already exists.");
            }

            if (mode == StorageWriteMode.Replace && existing is null)
            {
                throw new InvalidOperationException("Local storage record does not exist.");
            }

            if (request.ExpectedVersion.HasValue &&
                (existing?.Version ?? 0) != request.ExpectedVersion.Value)
            {
                throw new InvalidOperationException("Local storage record version did not match.");
            }

            var value = CreateStoredValue(request.Value);
            var record = new StorageRecord
            {
                Collection = collection,
                Key = key,
                Value = request.Value,
                ContentType = Normalize(request.ContentType),
                Attributes = CopyAttributes(request.Attributes),
                Version = (existing?.Version ?? 0) + 1,
                StoredAt = DateTimeOffset.UtcNow,
                ExpiresAt = request.ExpiresAt,
                CorrelationId = Normalize(request.CorrelationId)
            };
            WriteRecord(path, StoredStorageRecord.FromRecord(record, value), cancellationToken);
            return Task.FromResult(CopyRecord(record));
        }
    }

    public Task<StorageRecord?> GetAsync(
        StorageGetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var collection = ResolveCollection(request.Collection);
        var key = ResolveKey(request.Key);
        var path = GetRecordPath(collection, key);
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = ReadRecord(path, collection, key);
            if (record is null)
            {
                return Task.FromResult<StorageRecord?>(null);
            }

            if (record.ExpiresAt.HasValue &&
                record.ExpiresAt.Value <= DateTimeOffset.UtcNow &&
                request.IncludeExpired != true)
            {
                return Task.FromResult<StorageRecord?>(null);
            }

            return Task.FromResult<StorageRecord?>(CopyRecord(record));
        }
    }

    public Task<IReadOnlyList<StorageRecord>> QueryAsync(
        StorageQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var collection = ResolveCollection(request.Collection);
        var keyPrefix = Normalize(request.KeyPrefix);
        var attributes = CopyAttributes(request.Attributes);
        var limit = request.Limit ?? int.MaxValue;
        if (limit <= 0)
        {
            throw new InvalidOperationException(
                "Local storage query limit must be greater than zero.");
        }

        if (request.StoredFrom.HasValue &&
            request.StoredTo.HasValue &&
            request.StoredFrom.Value > request.StoredTo.Value)
        {
            throw new InvalidOperationException(
                "Local storage query storedFrom cannot be later than storedTo.");
        }

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = GetStorePath();
            if (!Directory.Exists(root))
            {
                return Task.FromResult<IReadOnlyList<StorageRecord>>([]);
            }

            var records = new List<StorageRecord>();
            foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var record = ReadRecord(path);
                if (record is null || !Matches(record, collection, keyPrefix, attributes, request))
                {
                    continue;
                }

                records.Add(CopyRecord(record));
            }

            return Task.FromResult<IReadOnlyList<StorageRecord>>(
                records
                    .OrderBy(record => record.StoredAt)
                    .ThenBy(record => record.Key, StringComparer.Ordinal)
                    .Take(limit)
                    .ToArray());
        }
    }

    public Task<StorageResult> DeleteAsync(
        StorageDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var collection = ResolveCollection(request.Collection);
        var key = ResolveKey(request.Key);
        var path = GetRecordPath(collection, key);
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = ReadRecord(path, collection, key);
            if (record is not null && File.Exists(path))
            {
                File.Delete(path);
            }

            return Task.FromResult(new StorageResult
            {
                Timestamp = DateTimeOffset.UtcNow,
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
            });
        }
    }

    private string ResolveCollection(string? value)
    {
        var collection = Normalize(value) ?? _settings.DefaultCollection;
        if (collection is null)
        {
            throw new InvalidOperationException(
                "Local storage request requires a collection.");
        }

        return collection;
    }

    private static string ResolveKey(string? value)
    {
        var key = Normalize(value);
        if (key is null)
        {
            throw new InvalidOperationException(
                "Local storage request requires a key.");
        }

        return key;
    }

    private StoredJsonValue? CreateStoredValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var element = JsonSerializer.SerializeToElement(value, SerializerOptions);
        var valueBytes = Encoding.UTF8.GetByteCount(element.GetRawText());
        if (valueBytes > _settings.MaxValueBytes)
        {
            throw new InvalidOperationException(
                $"Local storage value exceeds {_settings.MaxValueBytes} bytes.");
        }

        return new StoredJsonValue(element, valueBytes);
    }

    private StorageRecord? ReadRecord(
        string path,
        string collection,
        string key)
    {
        var record = ReadRecord(path);
        if (record is null)
        {
            return null;
        }

        if (!StringComparer.Ordinal.Equals(record.Collection, collection) ||
            !StringComparer.Ordinal.Equals(record.Key, key))
        {
            throw new InvalidOperationException(
                "Local storage record path did not match record identity.");
        }

        return record;
    }

    private StorageRecord? ReadRecord(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var document = JsonSerializer.Deserialize<StoredStorageRecord>(
            stream,
            SerializerOptions) ?? throw new InvalidOperationException(
                "Local storage record was empty.");
        if (document.FormatVersion != 1)
        {
            throw new InvalidOperationException(
                $"Local storage record format version '{document.FormatVersion}' is not supported.");
        }

        return document.ToRecord();
    }

    private void WriteRecord(
        string path,
        StoredStorageRecord document,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, document, SerializerOptions);
                if (_settings.FlushOnWrite)
                {
                    stream.Flush(flushToDisk: true);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private string GetRecordPath(string collection, string key)
        => Path.Combine(
            GetStorePath(),
            Hash(collection),
            $"{Hash(key)}.json");

    private string GetStorePath()
        => Path.Combine(
            _settings.RootDirectory,
            "stores",
            Hash(_settings.StoreName));

    private static bool Matches(
        StorageRecord record,
        string collection,
        string? keyPrefix,
        Dictionary<string, string> attributes,
        StorageQueryRequest request)
    {
        if (!StringComparer.Ordinal.Equals(record.Collection, collection))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(keyPrefix) &&
            !record.Key.StartsWith(keyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (request.StoredFrom.HasValue && record.StoredAt < request.StoredFrom.Value)
        {
            return false;
        }

        if (request.StoredTo.HasValue && record.StoredAt > request.StoredTo.Value)
        {
            return false;
        }

        if (record.ExpiresAt.HasValue &&
            record.ExpiresAt.Value <= DateTimeOffset.UtcNow &&
            request.IncludeExpired != true)
        {
            return false;
        }

        foreach (var (name, value) in attributes)
        {
            if (!record.Attributes.TryGetValue(name, out var current) ||
                !StringComparer.Ordinal.Equals(current, value))
            {
                return false;
            }
        }

        return true;
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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
        => source is null
            ? []
            : new Dictionary<string, string>(source, StringComparer.Ordinal);

    private static string? Normalize(string? value)
        => LocalStorageStoreOptions.Normalize(value);

    private sealed record StoredJsonValue(JsonElement Element, long ByteCount);

    private sealed record StoredStorageRecord
    {
        public int FormatVersion { get; init; } = 1;
        public required string Collection { get; init; }
        public required string Key { get; init; }
        public JsonElement? Value { get; init; }
        public long ValueBytes { get; init; }
        public string? ContentType { get; init; }
        public Dictionary<string, string> Attributes { get; init; } = [];
        public long Version { get; init; }
        public DateTimeOffset StoredAt { get; init; }
        public DateTimeOffset? ExpiresAt { get; init; }
        public string? CorrelationId { get; init; }

        public static StoredStorageRecord FromRecord(
            StorageRecord record,
            StoredJsonValue? value)
            => new()
            {
                Collection = record.Collection,
                Key = record.Key,
                Value = value?.Element,
                ValueBytes = value?.ByteCount ?? 0,
                ContentType = record.ContentType,
                Attributes = CopyAttributes(record.Attributes),
                Version = record.Version,
                StoredAt = record.StoredAt,
                ExpiresAt = record.ExpiresAt,
                CorrelationId = record.CorrelationId
            };

        public StorageRecord ToRecord()
            => new()
            {
                Collection = Collection,
                Key = Key,
                Value = ConvertValue(Value),
                ContentType = ContentType,
                Attributes = CopyAttributes(Attributes),
                Version = Version,
                StoredAt = StoredAt,
                ExpiresAt = ExpiresAt,
                CorrelationId = CorrelationId
            };

        private static object? ConvertValue(JsonElement? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            var element = value.Value;
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
    }
}
