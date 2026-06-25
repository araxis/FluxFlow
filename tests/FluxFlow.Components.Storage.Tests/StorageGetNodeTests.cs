using FluxFlow.Components.Storage;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Storage.Tests;

public sealed class StorageGetNodeTests
{
    [Fact]
    public async Task Get_FoundRoutesToResultAndFoundWithCorrelationId()
    {
        var store = new InMemoryStorageStore();
        await Seed(store, "items", "a", "one");
        await using var node = new StorageGetNode(store, new StorageGetOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var found = StorageTestSink.Link(node.Found);
        var notFound = StorageTestSink.Link(node.NotFound);

        var request = FlowMessage.Create(new StorageGetRequest { Key = "a", CorrelationId = "c-1" });
        await node.Input.SendAsync(request);

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.CorrelationId.ShouldBe(request.CorrelationId);
        result.Payload.Found.ShouldBeTrue();
        result.Payload.Record!.Value.ShouldBe("one");

        var foundResult = await found.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        foundResult.CorrelationId.ShouldBe(request.CorrelationId);
        foundResult.Payload.Record!.Value.ShouldBe("one");

        notFound.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Get_MissingRoutesToResultAndNotFoundWithCorrelationId()
    {
        var store = new InMemoryStorageStore();
        await using var node = new StorageGetNode(store, new StorageGetOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var found = StorageTestSink.Link(node.Found);
        var notFound = StorageTestSink.Link(node.NotFound);

        var request = FlowMessage.Create(new StorageGetRequest { Key = "missing", CorrelationId = "c-2" });
        await node.Input.SendAsync(request);

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.CorrelationId.ShouldBe(request.CorrelationId);
        result.Payload.Found.ShouldBeFalse();
        result.Payload.Succeeded.ShouldBeTrue();     // missing is a normal result, not an error
        result.Payload.Record.ShouldBeNull();

        var notFoundResult = await notFound.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        notFoundResult.CorrelationId.ShouldBe(request.CorrelationId);
        notFoundResult.Payload.Found.ShouldBeFalse();

        found.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Get_ReportsInvalidRequestWhenCollectionMissing()
    {
        var store = new InMemoryStorageStore();
        await using var node = new StorageGetNode(store);   // no default collection
        var errors = StorageTestSink.Link(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new StorageGetRequest { Key = "a" }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.InvalidRequest);
        error.Message.ShouldContain("collection");
    }

    [Fact]
    public async Task Get_ReportsStoreFailureAsErrorWithCorrelationId()
    {
        var store = new InMemoryStorageStore
        {
            FailWith = () => new InvalidOperationException("read failed")
        };
        await using var node = new StorageGetNode(store, new StorageGetOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var errors = StorageTestSink.Link(node.Errors);

        var request = FlowMessage.Create(new StorageGetRequest { Key = "a", CorrelationId = "c-3" });
        await node.Input.SendAsync(request);

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.GetFailed);
        error.CorrelationId.ShouldBe(request.CorrelationId);
        error.Message.ShouldContain("storage.get");
        error.Context.ShouldNotBeNull();
        error.Context.ShouldContain("operation=get");
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Get_ReportsMismatchedStoreRecordCollectionAsError()
    {
        var store = new StaticGetStore(new StorageRecord
        {
            Collection = "other",
            Key = "a",
            Value = "wrong",
            StoredAt = DateTimeOffset.UtcNow
        });
        await using var node = new StorageGetNode(store, new StorageGetOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var found = StorageTestSink.Link(node.Found);
        var errors = StorageTestSink.Link(node.Errors);

        var request = FlowMessage.Create(new StorageGetRequest { Key = "a", CorrelationId = "c-mismatch" });
        await node.Input.SendAsync(request);

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.GetFailed);
        error.CorrelationId.ShouldBe(request.CorrelationId);
        error.Message.ShouldContain("different collection");
        output.TryReceive(out _).ShouldBeFalse();
        found.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Get_ReportsMismatchedStoreRecordKeyAsError()
    {
        var store = new StaticGetStore(new StorageRecord
        {
            Collection = "items",
            Key = "b",
            Value = "wrong",
            StoredAt = DateTimeOffset.UtcNow
        });
        await using var node = new StorageGetNode(store, new StorageGetOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var errors = StorageTestSink.Link(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new StorageGetRequest { Key = "a" }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.GetFailed);
        error.Message.ShouldContain("different key");
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Get_EmitsFoundEvent()
    {
        var store = new InMemoryStorageStore();
        await Seed(store, "items", "a", "one");
        await using var node = new StorageGetNode(store, new StorageGetOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var events = StorageTestSink.Link(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(new StorageGetRequest { Key = "a" }));
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await node.DisposeAsync();

        var emitted = await StorageTestSink.DrainUntilCompletedAsync(events);
        emitted.ShouldHaveSingleItem().Name.ShouldBe(StorageDiagnosticNames.GetFound);
    }

    [Fact]
    public async Task Get_EmitsNotFoundEvent()
    {
        var store = new InMemoryStorageStore();
        await using var node = new StorageGetNode(store, new StorageGetOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var events = StorageTestSink.Link(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(new StorageGetRequest { Key = "x" }));
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await node.DisposeAsync();

        var emitted = await StorageTestSink.DrainUntilCompletedAsync(events);
        emitted.ShouldHaveSingleItem().Name.ShouldBe(StorageDiagnosticNames.GetNotFound);
    }

    private static async Task Seed(InMemoryStorageStore store, string collection, string key, object value)
        => await store.PutAsync(new StoragePutRequest { Collection = collection, Key = key, Value = value });

    private sealed class StaticGetStore(StorageRecord? record) : IStorageStore
    {
        public Task<StorageRecord> PutAsync(
            StoragePutRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StorageRecord?> GetAsync(
            StorageGetRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(record);

        public Task<IReadOnlyList<StorageRecord>> QueryAsync(
            StorageQueryRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StorageResult> DeleteAsync(
            StorageDeleteRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
