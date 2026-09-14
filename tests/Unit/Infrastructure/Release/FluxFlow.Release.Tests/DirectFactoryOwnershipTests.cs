using FluxFlow.Json.Model;
using FluxFlow.Components.Sessions.Composition;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Storage.Composition;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Definition;
using FluxFlow.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class DirectFactoryOwnershipTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var type in new[] { "session.record", "session.replay", "session.query", "storage.put", "storage.get", "storage.query", "storage.delete" })
        foreach (var owned in new[] { false, true })
        foreach (var invalid in new[] { false, true })
            yield return new object[] { type, owned, invalid };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Factories_preserve_lease_ownership_on_disposal_and_failed_graphs(string type, bool owned, bool invalid)
    {
        var store = new Store();
        var services = new ServiceCollection().AddSessions().AddStorage();
        services.AddKeyedSingleton<ISessionStoreFactory>("Resources.store", new SessionFactory(store, owned));
        services.AddKeyedSingleton<IStorageStoreFactory>("Resources.store", new StorageFactory(store, owned));
        await using var provider = services.BuildServiceProvider();
        var json = """{"Workflows":{"main":{"node":{"Type":"TYPE","store":"Resources.store","sessionId":"test" LINK}}}}"""
            .Replace("TYPE", type).Replace("LINK", invalid ? ", \"Output\":\"missing.Input\"" : "");
        var definition = ApplicationDefinitionJson.Deserialize(json);
        if (invalid)
            await Should.ThrowAsync<InvalidOperationException>(() => definition.BuildApplicationAsync(provider));
        else
        {
            var app = await definition.BuildApplicationAsync(provider);
            store.Disposals.ShouldBe(0);
            await app.DisposeAsync();
        }
        store.Disposals.ShouldBe(owned ? 1 : 0);
    }

    [Fact]
    public async Task Replay_constructor_failure_releases_acquired_owned_lease()
    {
        var store = new Store();
        await using var provider = new ServiceCollection().AddSessions()
            .AddKeyedSingleton<ISessionStoreFactory>("Resources.store", new SessionFactory(store, true)).BuildServiceProvider();
        var definition = ApplicationDefinitionJson.Deserialize("""{"Workflows":{"main":{"node":{"Type":"session.replay","store":"Resources.store"}}}}""");
        await Should.ThrowAsync<InvalidOperationException>(() => definition.BuildApplicationAsync(provider));
        store.Disposals.ShouldBe(1);
    }

    private sealed class SessionFactory(Store store, bool owned) : ISessionStoreFactory
    {
        public async ValueTask<SessionStoreLease> OpenAsync(SessionStoreContext context, CancellationToken cancellationToken = default)
        { await Task.Yield(); return owned ? SessionStoreLease.Owned(store) : SessionStoreLease.Shared(store); }
    }
    private sealed class StorageFactory(Store store, bool owned) : IStorageStoreFactory
    {
        public async ValueTask<StorageStoreLease> OpenAsync(StorageStoreContext context, CancellationToken cancellationToken = default)
        { await Task.Yield(); return owned ? StorageStoreLease.Owned(store) : StorageStoreLease.Shared(store); }
    }
    private sealed class Store : ISessionStore, IStorageStore, IAsyncDisposable
    {
        public int Disposals { get; private set; }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
        public Task<SessionMetadata?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SessionMetadata> StartSessionAsync(SessionStartRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SessionRecord> AppendMessageAsync(SessionAppendRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SessionMetadata> CompleteSessionAsync(SessionCompleteRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SessionMetadata>> QuerySessionsAsync(SessionQueryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<SessionRecord> ReadMessagesAsync(SessionReadRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StorageRecord> PutAsync(StoragePutRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StorageRecord?> GetAsync(StorageGetRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StorageRecord>> QueryAsync(StorageQueryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StorageResult> DeleteAsync(StorageDeleteRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
