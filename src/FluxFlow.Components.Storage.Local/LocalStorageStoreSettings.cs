namespace FluxFlow.Components.Storage.Local;

internal sealed record LocalStorageStoreSettings(
    string RootDirectory,
    string StoreName,
    string? DefaultCollection,
    long MaxValueBytes,
    bool FlushOnWrite);
