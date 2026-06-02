using FluxFlow.Components.Storage.Timing;

namespace FluxFlow.Components.Storage.FileSystem;

internal sealed record FileSystemStorageStoreSettings(
    string RootDirectory,
    string StoreName,
    string? DefaultCollection,
    long MaxValueBytes,
    bool FlushOnWrite,
    IStorageClock Clock);
