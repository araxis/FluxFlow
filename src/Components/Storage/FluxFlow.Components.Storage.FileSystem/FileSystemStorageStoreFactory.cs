using FluxFlow.Components.Storage.Contracts;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace FluxFlow.Components.Storage.FileSystem;

public sealed class FileSystemStorageStoreFactory : IStorageStoreFactory
{
    private readonly FileSystemStorageStoreOptions _options;
    private readonly ConcurrentDictionary<StoreKey, FileSystemStorageStore> _stores =
        new(StoreKeyComparer.Instance);

    public FileSystemStorageStoreFactory(FileSystemStorageStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ValueTask<StorageStoreLease> OpenAsync(
        StorageStoreContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var settings = _options.Resolve(context);
        var store = _stores.GetOrAdd(
            CreateStoreKey(settings),
            _ => new FileSystemStorageStore(settings));
        return ValueTask.FromResult(StorageStoreLease.Shared(store));
    }

    private static StoreKey CreateStoreKey(FileSystemStorageStoreSettings settings)
        => new(
            settings.RootDirectory,
            settings.StoreName,
            settings.DefaultCollection,
            settings.Clock);

    private sealed record StoreKey(
        string RootDirectory,
        string StoreName,
        string? DefaultCollection,
        TimeProvider Clock);

    private sealed class StoreKeyComparer : IEqualityComparer<StoreKey>
    {
        public static StoreKeyComparer Instance { get; } = new();

        private static readonly StringComparer RootDirectoryComparer =
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private StoreKeyComparer()
        {
        }

        public bool Equals(StoreKey? x, StoreKey? y)
        {
            if (ReferenceEquals(x, y))
                return true;

            if (x is null || y is null)
                return false;

            return RootDirectoryComparer.Equals(x.RootDirectory, y.RootDirectory) &&
                   StringComparer.Ordinal.Equals(x.StoreName, y.StoreName) &&
                   StringComparer.Ordinal.Equals(x.DefaultCollection, y.DefaultCollection) &&
                   ReferenceEquals(x.Clock, y.Clock);
        }

        public int GetHashCode(StoreKey obj)
        {
            var hash = new HashCode();
            hash.Add(obj.RootDirectory, RootDirectoryComparer);
            hash.Add(obj.StoreName, StringComparer.Ordinal);
            hash.Add(obj.DefaultCollection, StringComparer.Ordinal);
            hash.Add(RuntimeHelpers.GetHashCode(obj.Clock));
            return hash.ToHashCode();
        }
    }
}
