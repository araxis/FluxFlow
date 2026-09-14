using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Storage.Composition;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Composition;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Storage.Composition.Tests;

public sealed class StorageServiceCollectionExtensionsTests
{
    [Fact]
    public async Task Resolved_store_disposes_owned_lease_once_when_node_creation_fails()
    {
        var store = new InMemoryStorageStore();
        var resolved = ResolvedStorageStore.Leased(StorageStoreLease.Owned(store));
        var expected = new InvalidOperationException("Storage component activation sentinel.");
        IStorageStore? activatedStore = null;

        var actual = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await resolved.CreateNodeAsync<StoragePutNode>(storageStore =>
            {
                activatedStore = storageStore;
                throw expected;
            }));

        actual.ShouldBeSameAs(expected);
        activatedStore.ShouldBeSameAs(store);
        store.DisposeCount.ShouldBe(1);

        await resolved.DisposeAsync();

        store.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Resolved_store_transfers_owned_lease_cleanup_to_node_activation()
    {
        var store = new InMemoryStorageStore();
        var resolved = ResolvedStorageStore.Leased(StorageStoreLease.Owned(store));
        IStorageStore? activatedStore = null;

        var activation = await resolved.CreateNodeAsync(
            storageStore =>
            {
                activatedStore = storageStore;
                return new StoragePutNode(storageStore);
            });

        activatedStore.ShouldBeSameAs(store);
        Func<ValueTask> cleanup = resolved.DisposeAsync;
        store.DisposeCount.ShouldBe(0);

        await activation.DisposeAsync();
        await cleanup();
        store.DisposeCount.ShouldBe(1);

        await cleanup();
        store.DisposeCount.ShouldBe(1);
    }

    private sealed class InMemoryStorageStore : IStorageStore, IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly Dictionary<(string Collection, string Key), StorageRecord> _records = [];

        public int FailuresRemaining { get; set; }
        public int DisposeCount { get; private set; }

        public Task<StorageRecord> PutAsync(
            StoragePutRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfFailing();
            var collection = Required(request.Collection, "collection");
            var key = Required(request.Key, "key");
            lock (_gate)
            {
                _records.TryGetValue((collection, key), out var existing);
                var record = new StorageRecord
                {
                    Collection = collection,
                    Key = key,
                    Value = request.Value,
                    ContentType = request.ContentType,
                    Attributes = new Dictionary<string, string>(request.Attributes),
                    Version = (existing?.Version ?? 0) + 1,
                    StoredAt = DateTimeOffset.UtcNow,
                    ExpiresAt = request.ExpiresAt,
                    CorrelationId = request.CorrelationId
                };
                _records[(collection, key)] = record;
                return Task.FromResult(Copy(record));
            }
        }

        public Task<StorageRecord?> GetAsync(
            StorageGetRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfFailing();
            lock (_gate)
            {
                return Task.FromResult(_records.TryGetValue(
                    (Required(request.Collection, "collection"), Required(request.Key, "key")),
                    out var record)
                    ? Copy(record)
                    : null);
            }
        }

        public Task<IReadOnlyList<StorageRecord>> QueryAsync(
            StorageQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfFailing();
            var collection = Required(request.Collection, "collection");
            lock (_gate)
            {
                IReadOnlyList<StorageRecord> result = _records.Values
                    .Where(record => StringComparer.Ordinal.Equals(record.Collection, collection))
                    .Where(record => StorageQueryMatcher.IsMatch(record, request, DateTimeOffset.UtcNow))
                    .OrderBy(record => record.StoredAt)
                    .ThenBy(record => record.Key, StringComparer.Ordinal)
                    .Skip(request.Offset ?? 0)
                    .Take(request.Limit ?? int.MaxValue)
                    .Select(Copy)
                    .ToArray();
                return Task.FromResult(result);
            }
        }

        public Task<StorageResult> DeleteAsync(
            StorageDeleteRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfFailing();
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
                    Version = record?.Version
                });
            }
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        private void ThrowIfFailing()
        {
            if (FailuresRemaining <= 0)
                return;
            FailuresRemaining--;
            throw new InvalidOperationException("Store failure.");
        }

        private static StorageRecord Copy(StorageRecord record)
            => record with
            {
                Attributes = new Dictionary<string, string>(record.Attributes)
            };

        private static string Required(string? value, string name)
            => string.IsNullOrWhiteSpace(value)
                ? throw new InvalidOperationException($"Storage request requires {name}.")
                : value.Trim();
    }
}
