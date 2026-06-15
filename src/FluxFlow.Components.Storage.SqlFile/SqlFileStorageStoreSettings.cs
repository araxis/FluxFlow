namespace FluxFlow.Components.Storage.SqlFile;

internal sealed record SqlFileStorageStoreSettings(
    string DatabasePath,
    string StoreName,
    string? DefaultCollection,
    long MaxValueBytes,
    int BusyTimeoutMilliseconds,
    bool CreateDatabase,
    TimeProvider Clock);
