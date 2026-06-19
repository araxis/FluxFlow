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
    public string? StoreName { get; init; }
    public string? Collection { get; init; }
    public TimeProvider Clock { get; init; } = TimeProvider.System;
}
