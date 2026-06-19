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

public sealed class StorageDeleteNodeTests
{
    [Fact]
    public async Task Delete_RemovesRecordAndPreservesCorrelationId()
    {
        var store = new InMemoryStorageStore();
        await Seed(store, "items", "a", "one");
        await using var node = new StorageDeleteNode(store, new StorageDeleteOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);

        var request = FlowMessage.Create(new StorageDeleteRequest { Key = "a", CorrelationId = "c-1" });
        await node.Input.SendAsync(request);

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.CorrelationId.ShouldBe(request.CorrelationId);
        result.Payload.Found.ShouldBeTrue();
        result.Payload.Deleted.ShouldBeTrue();
        result.Payload.Key.ShouldBe("a");
        store.RecordCount.ShouldBe(0);
    }

    [Fact]
    public async Task Delete_MissingEmitsResultByDefault()
    {
        var store = new InMemoryStorageStore();
        await using var node = new StorageDeleteNode(store, new StorageDeleteOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new StorageDeleteRequest { Key = "missing" }));

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.Payload.Found.ShouldBeFalse();
        result.Payload.Deleted.ShouldBeFalse();
        result.Payload.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_SuppressesMissingResultWhenConfigured()
    {
        var store = new InMemoryStorageStore();
        await Seed(store, "items", "present", "x");
        await using var node = new StorageDeleteNode(
            store,
            new StorageDeleteOptions { Collection = "items", EmitMissingAsResult = false });
        var output = StorageTestSink.Link(node.Output);

        // Missing delete is suppressed; the following present delete still emits, so the
        // first item we receive is the present one — proving the missing one was dropped.
        await node.Input.SendAsync(FlowMessage.Create(new StorageDeleteRequest { Key = "missing" }));
        await node.Input.SendAsync(FlowMessage.Create(new StorageDeleteRequest { Key = "present" }));

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.Payload.Key.ShouldBe("present");
        result.Payload.Found.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_ReportsInvalidRequestWhenKeyMissing()
    {
        var store = new InMemoryStorageStore();
        await using var node = new StorageDeleteNode(store, new StorageDeleteOptions { Collection = "items" });
        var errors = StorageTestSink.Link(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new StorageDeleteRequest { Key = "" }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task Delete_ReportsStoreFailureAsErrorWithCorrelationId()
    {
        var store = new InMemoryStorageStore
        {
            FailWith = () => new InvalidOperationException("delete failed")
        };
        await using var node = new StorageDeleteNode(store, new StorageDeleteOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var errors = StorageTestSink.Link(node.Errors);

        var request = FlowMessage.Create(new StorageDeleteRequest { Key = "a", CorrelationId = "c-5" });
        await node.Input.SendAsync(request);

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(StorageErrorCodes.DeleteFailed);
        error.CorrelationId.ShouldBe(request.CorrelationId);
        error.Message.ShouldContain("storage.delete");
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_EmitsDeletedEvent()
    {
        var store = new InMemoryStorageStore();
        await Seed(store, "items", "a", "one");
        await using var node = new StorageDeleteNode(store, new StorageDeleteOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var events = StorageTestSink.Link(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(new StorageDeleteRequest { Key = "a" }));
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await node.DisposeAsync();

        (await StorageTestSink.DrainUntilCompletedAsync(events))
            .ShouldHaveSingleItem().Name.ShouldBe(StorageDiagnosticNames.DeleteDeleted);
    }

    [Fact]
    public async Task FourNodes_RoundTripThroughOneSharedStore()
    {
        // The host owns one store and injects it into every operation node.
        var store = new InMemoryStorageStore();

        await using var put = new StoragePutNode(store, new StoragePutOptions { Collection = "items" });
        var putOutput = StorageTestSink.Link(put.Output);
        await put.Input.SendAsync(FlowMessage.Create(new StoragePutRequest { Key = "a", Value = "one" }));
        (await putOutput.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload.Version.ShouldBe(1);
        store.RecordCount.ShouldBe(1);

        await using var get = new StorageGetNode(store, new StorageGetOptions { Collection = "items" });
        var getOutput = StorageTestSink.Link(get.Output);
        await get.Input.SendAsync(FlowMessage.Create(new StorageGetRequest { Key = "a" }));
        (await getOutput.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload.Record!.Value.ShouldBe("one");

        await using var query = new StorageQueryNode(store, new StorageQueryOptions { Collection = "items" });
        var queryOutput = StorageTestSink.Link(query.Output);
        await query.Input.SendAsync(FlowMessage.Create(new StorageQueryRequest()));
        (await queryOutput.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload.Count.ShouldBe(1);

        await using var delete = new StorageDeleteNode(store, new StorageDeleteOptions { Collection = "items" });
        var deleteOutput = StorageTestSink.Link(delete.Output);
        await delete.Input.SendAsync(FlowMessage.Create(new StorageDeleteRequest { Key = "a" }));
        (await deleteOutput.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload.Deleted.ShouldBeTrue();
        store.RecordCount.ShouldBe(0);
    }

    private static async Task Seed(InMemoryStorageStore store, string collection, string key, object value)
        => await store.PutAsync(new StoragePutRequest { Collection = collection, Key = key, Value = value });
}
