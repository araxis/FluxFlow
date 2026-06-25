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

public sealed class StorageQueryNodeTests
{
    [Fact]
    public async Task Query_EmitsResultAndRecordsWithCorrelationId()
    {
        var store = new InMemoryStorageStore();
        await Seed(store, "items", "a", "one");
        await Seed(store, "items", "b", "two");
        await using var node = new StorageQueryNode(store, new StorageQueryOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var records = StorageTestSink.Link(node.Records);

        var request = FlowMessage.Create(new StorageQueryRequest { CorrelationId = "q-1" });
        await node.Input.SendAsync(request);

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.CorrelationId.ShouldBe(request.CorrelationId);
        result.Payload.Count.ShouldBe(2);
        result.Payload.Operation.ShouldBe("query");
        result.Payload.Records.Select(r => r.Key).ShouldBe(["a", "b"]);

        var first = await records.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        first.CorrelationId.ShouldBe(request.CorrelationId);
        first.Payload.Key.ShouldBe("a");
        var second = await records.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        second.CorrelationId.ShouldBe(request.CorrelationId);
        second.Payload.Key.ShouldBe("b");
    }

    [Fact]
    public async Task Query_SuppressesRecordsInResultWhenConfigured()
    {
        var store = new InMemoryStorageStore();
        await Seed(store, "items", "a", "one");
        await using var node = new StorageQueryNode(
            store,
            new StorageQueryOptions { Collection = "items", EmitRecordsInResult = false });
        var output = StorageTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new StorageQueryRequest()));

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.Payload.Count.ShouldBe(1);            // count still reflects matches
        result.Payload.Records.ShouldBeEmpty();      // but the records collection is suppressed
    }

    [Fact]
    public async Task Query_SuppressesRecordOutputsWhenConfigured()
    {
        var store = new InMemoryStorageStore();
        await Seed(store, "items", "a", "one");
        await using var node = new StorageQueryNode(
            store,
            new StorageQueryOptions { Collection = "items", EmitRecordOutputs = false });
        var output = StorageTestSink.Link(node.Output);
        var records = StorageTestSink.Link(node.Records);

        await node.Input.SendAsync(FlowMessage.Create(new StorageQueryRequest()));
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await node.DisposeAsync();

        (await StorageTestSink.DrainUntilCompletedAsync(records)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Query_HonorsLimit()
    {
        var store = new InMemoryStorageStore();
        await Seed(store, "items", "a", "one");
        await Seed(store, "items", "b", "two");
        await Seed(store, "items", "c", "three");
        await using var node = new StorageQueryNode(
            store,
            new StorageQueryOptions { Collection = "items", Limit = 2 });
        var output = StorageTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new StorageQueryRequest()));

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.Payload.Count.ShouldBe(2);
        result.Payload.Records.Select(r => r.Key).ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task Query_FiltersByKeyPrefix()
    {
        var store = new InMemoryStorageStore();
        await Seed(store, "items", "user:1", "one");
        await Seed(store, "items", "user:2", "two");
        await Seed(store, "items", "order:1", "three");
        await using var node = new StorageQueryNode(store, new StorageQueryOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new StorageQueryRequest { KeyPrefix = "user:" }));

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.Payload.Records.Select(r => r.Key).ShouldBe(["user:1", "user:2"]);
    }

    [Fact]
    public async Task Query_ReportsInvalidRequestForBadLimit()
    {
        var store = new InMemoryStorageStore();
        await using var node = new StorageQueryNode(store, new StorageQueryOptions { Collection = "items" });
        var errors = StorageTestSink.Link(node.Errors);

        var request = FlowMessage.Create(new StorageQueryRequest { Limit = 0, CorrelationId = "q-bad" });
        await node.Input.SendAsync(request);

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.InvalidRequest);
        error.CorrelationId.ShouldBe(request.CorrelationId);
        error.Message.ShouldContain("limit");
        error.Context.ShouldNotBeNull();
        error.Context.ShouldContain("operation=query");
        error.Context.ShouldContain("correlationId=q-bad");
    }

    [Fact]
    public async Task Query_ReportsInvalidRequestForNegativeOffset()
    {
        var store = new InMemoryStorageStore();
        await using var node = new StorageQueryNode(store, new StorageQueryOptions { Collection = "items" });
        var errors = StorageTestSink.Link(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new StorageQueryRequest { Offset = -1 }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.InvalidRequest);
        error.Message.ShouldContain("offset");
    }

    [Fact]
    public async Task Query_ReportsStoreFailureAsErrorWithCorrelationId()
    {
        var store = new InMemoryStorageStore
        {
            FailWith = () => new InvalidOperationException("query failed")
        };
        await using var node = new StorageQueryNode(store, new StorageQueryOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var errors = StorageTestSink.Link(node.Errors);

        var request = FlowMessage.Create(new StorageQueryRequest { CorrelationId = "q-9" });
        await node.Input.SendAsync(request);

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.QueryFailed);
        error.CorrelationId.ShouldBe(request.CorrelationId);
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Query_ReportsStoreRecordOutsideFilterAsError()
    {
        var store = new StaticQueryStore([
            new StorageRecord
            {
                Collection = "items",
                Key = "order:1",
                Value = "wrong",
                StoredAt = DateTimeOffset.UtcNow
            }
        ]);
        await using var node = new StorageQueryNode(store, new StorageQueryOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var records = StorageTestSink.Link(node.Records);
        var errors = StorageTestSink.Link(node.Errors);

        var request = FlowMessage.Create(new StorageQueryRequest
        {
            KeyPrefix = "user:",
            CorrelationId = "q-mismatch"
        });
        await node.Input.SendAsync(request);

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.QueryFailed);
        error.CorrelationId.ShouldBe(request.CorrelationId);
        error.Message.ShouldContain("does not match");
        output.TryReceive(out _).ShouldBeFalse();
        records.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Query_ReportsStoreLimitOverrunAsError()
    {
        var store = new StaticQueryStore([
            new StorageRecord
            {
                Collection = "items",
                Key = "a",
                Value = "one",
                StoredAt = DateTimeOffset.UtcNow
            },
            new StorageRecord
            {
                Collection = "items",
                Key = "b",
                Value = "two",
                StoredAt = DateTimeOffset.UtcNow
            }
        ]);
        await using var node = new StorageQueryNode(store, new StorageQueryOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var errors = StorageTestSink.Link(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new StorageQueryRequest { Limit = 1 }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.QueryFailed);
        error.Message.ShouldContain("requested limit");
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Query_ReportsNullStoreRecordCollectionAsError()
    {
        var store = new StaticQueryStore(null);
        await using var node = new StorageQueryNode(store, new StorageQueryOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var errors = StorageTestSink.Link(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new StorageQueryRequest()));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.QueryFailed);
        error.Message.ShouldContain("null record collection");
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Query_ReportsNullStoreRecordAsError()
    {
        var store = new StaticQueryStore([null!]);
        await using var node = new StorageQueryNode(store, new StorageQueryOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var records = StorageTestSink.Link(node.Records);
        var errors = StorageTestSink.Link(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new StorageQueryRequest()));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.QueryFailed);
        error.Message.ShouldContain("null record");
        output.TryReceive(out _).ShouldBeFalse();
        records.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Query_EmitsCompletedEvent()
    {
        var store = new InMemoryStorageStore();
        await Seed(store, "items", "a", "one");
        await using var node = new StorageQueryNode(store, new StorageQueryOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var events = StorageTestSink.Link(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(new StorageQueryRequest()));
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await node.DisposeAsync();

        var emitted = await StorageTestSink.DrainUntilCompletedAsync(events);
        var @event = emitted.ShouldHaveSingleItem();
        @event.Name.ShouldBe(StorageDiagnosticNames.QueryCompleted);
        @event.Attributes["count"].ShouldBe(1);
    }

    private static async Task Seed(InMemoryStorageStore store, string collection, string key, object value)
        => await store.PutAsync(new StoragePutRequest { Collection = collection, Key = key, Value = value });

    private sealed class StaticQueryStore(IReadOnlyList<StorageRecord>? records) : IStorageStore
    {
        public Task<StorageRecord> PutAsync(
            StoragePutRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StorageRecord?> GetAsync(
            StorageGetRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<StorageRecord>> QueryAsync(
            StorageQueryRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(records!);

        public Task<StorageResult> DeleteAsync(
            StorageDeleteRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
