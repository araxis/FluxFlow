using FluxFlow.Components.Storage.Contracts;

namespace FluxFlow.StorageCompositionSample;

internal sealed class InMemoryStorageStore : IStorageStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Collection, string Key), StorageRecord> _records = [];

    public int RecordCount
    {
        get
        {
            lock (_gate)
            {
                return _records.Count;
            }
        }
    }

    public Task<StorageRecord> PutAsync(
        StoragePutRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var collection = Required(request.Collection, "collection");
        var key = Required(request.Key, "key");
        lock (_gate)
        {
            _records.TryGetValue((collection, key), out var existing);
            var mode = request.Mode ?? StorageWriteMode.Upsert;
            if (mode == StorageWriteMode.Create && existing is not null)
            {
                throw new InvalidOperationException("Record already exists.");
            }

            if (mode == StorageWriteMode.Replace && existing is null)
            {
                throw new InvalidOperationException("Record does not exist.");
            }

            if (request.ExpectedVersion.HasValue &&
                (existing?.Version ?? 0) != request.ExpectedVersion.Value)
            {
                throw new InvalidOperationException("Record version did not match.");
            }

            var record = new StorageRecord
            {
                Collection = collection,
                Key = key,
                Value = request.Value,
                ContentType = request.ContentType,
                Attributes = CopyAttributes(request.Attributes),
                Version = (existing?.Version ?? 0) + 1,
                StoredAt = DateTimeOffset.UtcNow,
                ExpiresAt = request.ExpiresAt,
                CorrelationId = request.CorrelationId
            };
            _records[(collection, key)] = record;
            return Task.FromResult(CopyRecord(record));
        }
    }

    public Task<StorageRecord?> GetAsync(
        StorageGetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var collection = Required(request.Collection, "collection");
        var key = Required(request.Key, "key");
        lock (_gate)
        {
            if (!_records.TryGetValue((collection, key), out var record))
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

    public Task<StorageResult> DeleteAsync(
        StorageDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var collection = Required(request.Collection, "collection");
        var key = Required(request.Key, "key");
        lock (_gate)
        {
            var found = _records.Remove((collection, key), out var record);
            return Task.FromResult(new StorageResult
            {
                Timestamp = DateTimeOffset.UtcNow,
                Operation = "delete",
                Collection = collection,
                Key = key,
                Succeeded = true,
                Found = found,
                Deleted = found,
                Record = record is null ? null : CopyRecord(record),
                Version = record?.Version,
                CorrelationId = request.CorrelationId,
                Attributes = record is null ? [] : CopyAttributes(record.Attributes)
            });
        }
    }

    private static string Required(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Storage request requires {name}.");
        }

        return value.Trim();
    }

    private static StorageRecord CopyRecord(StorageRecord record)
        => record with
        {
            Attributes = CopyAttributes(record.Attributes)
        };

    private static Dictionary<string, string> CopyAttributes(
        Dictionary<string, string>? source)
        => source is null
            ? []
            : new Dictionary<string, string>(source, StringComparer.Ordinal);
}
