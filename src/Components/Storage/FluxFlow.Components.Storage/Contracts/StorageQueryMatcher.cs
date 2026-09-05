namespace FluxFlow.Components.Storage.Contracts;

public static class StorageQueryMatcher
{
    public static void Validate(StorageQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Offset is < 0)
        {
            throw new InvalidOperationException(
                "storage.query request offset cannot be negative.");
        }

        if (request.Limit is <= 0)
        {
            throw new InvalidOperationException(
                "storage.query request limit must be greater than zero.");
        }

        if (request.StoredFrom.HasValue &&
            request.StoredTo.HasValue &&
            request.StoredFrom.Value > request.StoredTo.Value)
        {
            throw new InvalidOperationException(
                "storage.query request storedFrom cannot be later than storedTo.");
        }
    }

    public static bool IsMatch(
        StorageRecord record,
        StorageQueryRequest request,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrWhiteSpace(request.Collection) &&
            !StringComparer.Ordinal.Equals(record.Collection, request.Collection))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.KeyPrefix) &&
            !record.Key.StartsWith(request.KeyPrefix, StringComparison.Ordinal))
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
            record.ExpiresAt.Value <= now &&
            request.IncludeExpired != true)
        {
            return false;
        }

        if (request.Attributes is null)
        {
            return true;
        }

        foreach (var (name, value) in request.Attributes)
        {
            if (record.Attributes is null ||
                !record.Attributes.TryGetValue(name, out var current) ||
                !StringComparer.Ordinal.Equals(current, value))
            {
                return false;
            }
        }

        return true;
    }
}
