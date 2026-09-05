namespace FluxFlow.Components.Storage.Contracts;

/// <summary>
/// The context an <see cref="IStorageStoreFactory"/> receives when the host opens a
/// store: the logical store name, an optional default collection, and the clock to
/// use for stored timestamps and expiration checks. Engine-free — the host owns the
/// <see cref="IStorageStore"/> lifetime and injects the opened store into the
/// operation nodes directly.
/// </summary>
public sealed record StorageStoreContext
{
    private string? _storeName;
    private string? _collection;
    private TimeProvider _clock = TimeProvider.System;

    public string? StoreName
    {
        get => _storeName;
        init => _storeName = Normalize(value);
    }

    public string? Collection
    {
        get => _collection;
        init => _collection = Normalize(value);
    }

    public TimeProvider Clock
    {
        get => _clock;
        init => _clock = value ?? TimeProvider.System;
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
