using FluxFlow.Components.Storage.Contracts;

namespace FluxFlow.Components.Storage.Nodes;

internal static class StorageNodeSupport
{
    public static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static Dictionary<string, string> CopyAttributes(Dictionary<string, string>? source)
        => source is null
            ? []
            : new Dictionary<string, string>(source, StringComparer.Ordinal);

    public static string ResolveCollection(
        string nodeType,
        string? requestCollection,
        string? defaultCollection)
    {
        var collection = Normalize(requestCollection) ?? Normalize(defaultCollection);
        if (string.IsNullOrWhiteSpace(collection))
        {
            throw new InvalidOperationException(
                $"{nodeType} requires a collection on the request or node options.");
        }

        return collection;
    }

    public static string ResolveKey(string nodeType, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException($"{nodeType} request key cannot be empty.");
        }

        return key.Trim();
    }

    public static StorageResult CreateRecordResult(
        string operation,
        StorageRecord record,
        bool includeRecord,
        string? correlationId,
        TimeProvider clock)
        => new()
        {
            Timestamp = clock.GetUtcNow(),
            Operation = operation,
            Collection = record.Collection,
            Key = record.Key,
            Succeeded = true,
            Found = true,
            Record = includeRecord ? CopyRecord(record) : null,
            Version = record.Version,
            CorrelationId = Normalize(correlationId) ?? Normalize(record.CorrelationId),
            Attributes = CopyAttributes(record.Attributes)
        };

    public static StorageRecord CopyRecord(StorageRecord record)
        => record with
        {
            Attributes = CopyAttributes(record.Attributes)
        };

    public static Dictionary<string, object?> CreateOperationAttributes(
        string operation,
        string collection,
        string key,
        string? correlationId,
        long? version = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["operation"] = operation,
            ["collection"] = collection,
            ["key"] = key
        };

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            attributes["correlationId"] = correlationId;
        }

        if (version.HasValue)
        {
            attributes["version"] = version.Value;
        }

        return attributes;
    }

    public static Dictionary<string, object?> CreateCollectionAttributes(
        string operation,
        string collection,
        string? correlationId,
        int? count = null,
        int? limit = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["operation"] = operation,
            ["collection"] = collection
        };

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            attributes["correlationId"] = correlationId;
        }

        if (count.HasValue)
        {
            attributes["count"] = count.Value;
        }

        if (limit.HasValue)
        {
            attributes["limit"] = limit.Value;
        }

        return attributes;
    }

    public static string CreateOperationContext(
        string operation,
        string collection,
        string key,
        string? correlationId)
    {
        var values = new List<string>
        {
            $"operation={operation}",
            $"collection={collection}",
            $"key={key}"
        };

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            values.Add($"correlationId={correlationId}");
        }

        return string.Join("; ", values);
    }

    public static string CreateCollectionContext(
        string operation,
        string collection,
        string? correlationId)
    {
        var values = new List<string>
        {
            $"operation={operation}",
            $"collection={collection}"
        };

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            values.Add($"correlationId={correlationId}");
        }

        return string.Join("; ", values);
    }
}
