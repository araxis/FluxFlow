using System.Text;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Storage.Tests;

public sealed class FlowContentStorageNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Put_and_get_preserve_exact_content_metadata_and_lineage()
    {
        var store = new InMemoryStorageStore();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-20T14:00:00Z"));
        var attributes = new Dictionary<string, string> { ["tenant"] = "north" };
        byte[] bytes = [0x00, 0x7F, 0xFF];
        await using var put = new FlowContentStoragePutNode(
            store,
            new StoragePutOptions { Collection = "items" },
            clock);
        var putOutput = StorageTestSink.Link(put.Output);
        var input = FlowMessage.Create(
            new StorageContentPutRequest
            {
                Key = "a",
                Content = FlowContent.FromBytes(
                    bytes,
                    "application/vnd.example.record",
                    "binary"),
                Attributes = attributes
            },
            new CorrelationId("storage-content"),
            new TraceId("storage-trace"));
        attributes["tenant"] = "changed";

        (await put.Input.SendAsync(input)).ShouldBeTrue();

        var putMessage = await putOutput.ReceiveAsync().WaitAsync(Timeout);
        putMessage.CorrelationId.ShouldBe(input.CorrelationId);
        putMessage.TraceId.ShouldBe(input.TraceId);
        putMessage.CausationId.ShouldBe(input.MessageId);
        putMessage.Payload.Kind.ShouldBe(StorageResultKinds.PutStored);
        putMessage.Payload.IsError.ShouldBeFalse();
        var stored = putMessage.Payload.Value.ShouldNotBeNull();
        stored.Record.ShouldNotBeNull().Content.OriginalBytes.AsSpan().ToArray().ShouldBe(bytes);
        stored.Record.Content.ContentType.ShouldBe("application/vnd.example.record");
        stored.Record.Content.Encoding.ShouldBe("binary");
        stored.Record.Attributes["tenant"].ShouldBe("north");

        await using var get = new FlowContentStorageGetNode(
            store,
            new StorageGetOptions { Collection = "items" },
            clock);
        var getOutput = StorageTestSink.Link(get.Output);
        var getInput = FlowMessage.Create(
            new StorageGetRequest { Key = "a" },
            input.CorrelationId,
            input.TraceId);

        (await get.Input.SendAsync(getInput)).ShouldBeTrue();

        var getMessage = await getOutput.ReceiveAsync().WaitAsync(Timeout);
        getMessage.CausationId.ShouldBe(getInput.MessageId);
        getMessage.Payload.Kind.ShouldBe(StorageResultKinds.GetFound);
        var found = getMessage.Payload.Value.ShouldNotBeNull();
        found.Found.ShouldBeTrue();
        found.Record.ShouldNotBeNull().Content.OriginalBytes.AsSpan().ToArray().ShouldBe(bytes);
    }

    [Fact]
    public async Task Value_only_put_is_normal_failure_and_later_input_continues()
    {
        var store = new InMemoryStorageStore();
        await using var node = new FlowContentStoragePutNode(
            store,
            new StoragePutOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new StorageContentPutRequest
        {
            Key = "invalid",
            Content = FlowContent.FromValue(FlowValue.From("serialize upstream"))
        }));
        await node.Input.SendAsync(FlowMessage.Create(new StorageContentPutRequest
        {
            Key = "valid",
            Content = FlowContent.FromBytes(Encoding.UTF8.GetBytes("ok"), "text/plain", "utf-8")
        }));

        var failure = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        failure.Kind.ShouldBe(StorageResultKinds.PutFailed);
        failure.Error.ShouldNotBeNull().Code.ShouldBe(StorageErrorCodeNames.ContentUnavailable);
        var success = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        success.Kind.ShouldBe(StorageResultKinds.PutStored);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Invalid_requests_are_normal_invalid_request_results()
    {
        var store = new InMemoryStorageStore();
        await using var put = new FlowContentStoragePutNode(
            store,
            new StoragePutOptions { Collection = "items" });
        await using var get = new FlowContentStorageGetNode(
            store,
            new StorageGetOptions { Collection = "items" });
        await using var query = new FlowContentStorageQueryNode(
            store,
            new StorageQueryOptions { Collection = "items" });
        await using var delete = new FlowContentStorageDeleteNode(
            store,
            new StorageDeleteOptions { Collection = "items" });
        var putOutput = StorageTestSink.Link(put.Output);
        var getOutput = StorageTestSink.Link(get.Output);
        var queryOutput = StorageTestSink.Link(query.Output);
        var deleteOutput = StorageTestSink.Link(delete.Output);

        await put.Input.SendAsync(FlowMessage.Create(new StorageContentPutRequest
        {
            Key = " ",
            Content = FlowContent.FromBytes(new byte[] { 1 })
        }));
        await get.Input.SendAsync(FlowMessage.Create(new StorageGetRequest { Key = " " }));
        await query.Input.SendAsync(FlowMessage.Create(new StorageQueryRequest { Offset = -1 }));
        await delete.Input.SendAsync(FlowMessage.Create(new StorageDeleteRequest { Key = " " }));

        (await putOutput.ReceiveAsync().WaitAsync(Timeout)).Payload.Error
            .ShouldNotBeNull().Code.ShouldBe(StorageErrorCodeNames.InvalidRequest);
        (await getOutput.ReceiveAsync().WaitAsync(Timeout)).Payload.Error
            .ShouldNotBeNull().Code.ShouldBe(StorageErrorCodeNames.InvalidRequest);
        (await queryOutput.ReceiveAsync().WaitAsync(Timeout)).Payload.Error
            .ShouldNotBeNull().Code.ShouldBe(StorageErrorCodeNames.InvalidRequest);
        (await deleteOutput.ReceiveAsync().WaitAsync(Timeout)).Payload.Error
            .ShouldNotBeNull().Code.ShouldBe(StorageErrorCodeNames.InvalidRequest);
        store.RecordCount.ShouldBe(0);
    }

    [Fact]
    public async Task Get_missing_and_legacy_content_are_normal_results()
    {
        var store = new InMemoryStorageStore();
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "legacy",
            Value = "legacy-value"
        });
        await using var node = new FlowContentStorageGetNode(
            store,
            new StorageGetOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new StorageGetRequest { Key = "missing" }));
        await node.Input.SendAsync(FlowMessage.Create(new StorageGetRequest { Key = "legacy" }));

        var missing = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        missing.Kind.ShouldBe(StorageResultKinds.GetNotFound);
        missing.IsError.ShouldBeFalse();
        missing.Value.ShouldNotBeNull().Found.ShouldBeFalse();

        var invalid = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        invalid.Kind.ShouldBe(StorageResultKinds.GetFailed);
        invalid.Error.ShouldNotBeNull().Code.ShouldBe(StorageErrorCodeNames.StoredContentInvalid);
    }

    [Fact]
    public async Task Query_returns_one_content_result_without_record_branching()
    {
        var store = new InMemoryStorageStore();
        await using (var put = new FlowContentStoragePutNode(
                         store,
                         new StoragePutOptions { Collection = "items" }))
        {
            var putOutput = StorageTestSink.Link(put.Output);
            foreach (var key in new[] { "a", "b" })
            {
                await put.Input.SendAsync(FlowMessage.Create(new StorageContentPutRequest
                {
                    Key = key,
                    Content = FlowContent.FromBytes(Encoding.UTF8.GetBytes(key), "text/plain")
                }));
                await putOutput.ReceiveAsync().WaitAsync(Timeout);
            }
        }

        await using var query = new FlowContentStorageQueryNode(
            store,
            new StorageQueryOptions { Collection = "items" });
        var queryOutput = StorageTestSink.Link(query.Output);
        await query.Input.SendAsync(FlowMessage.Create(new StorageQueryRequest()));

        var result = (await queryOutput.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.Kind.ShouldBe(StorageResultKinds.QueryCompleted);
        result.Value.ShouldNotBeNull().Count.ShouldBe(2);
        result.Value.Records.Select(record => record.Key).ShouldBe(["a", "b"]);
        result.Value.Records[0].Content.OriginalBytes.AsSpan().ToArray()
            .ShouldBe(Encoding.UTF8.GetBytes("a"));
    }

    [Fact]
    public async Task Delete_always_returns_deleted_or_missing_outcome()
    {
        var store = new InMemoryStorageStore();
        await using (var put = new FlowContentStoragePutNode(
                         store,
                         new StoragePutOptions { Collection = "items" }))
        {
            var putOutput = StorageTestSink.Link(put.Output);
            await put.Input.SendAsync(FlowMessage.Create(new StorageContentPutRequest
            {
                Key = "a",
                Content = FlowContent.FromBytes(new byte[] { 1 })
            }));
            await putOutput.ReceiveAsync().WaitAsync(Timeout);
        }

        await using var delete = new FlowContentStorageDeleteNode(
            store,
            new StorageDeleteOptions
            {
                Collection = "items",
                EmitMissingAsResult = false
            });
        var deleteOutput = StorageTestSink.Link(delete.Output);

        await delete.Input.SendAsync(FlowMessage.Create(new StorageDeleteRequest { Key = "a" }));
        await delete.Input.SendAsync(FlowMessage.Create(new StorageDeleteRequest { Key = "a" }));

        var deleted = (await deleteOutput.ReceiveAsync().WaitAsync(Timeout)).Payload;
        deleted.Kind.ShouldBe(StorageResultKinds.DeleteDeleted);
        deleted.Value.ShouldNotBeNull().Deleted.ShouldBeTrue();
        var missing = (await deleteOutput.ReceiveAsync().WaitAsync(Timeout)).Payload;
        missing.Kind.ShouldBe(StorageResultKinds.DeleteNotFound);
        missing.IsError.ShouldBeFalse();
        missing.Value.ShouldNotBeNull().Found.ShouldBeFalse();
    }

    [Fact]
    public async Task Store_failure_is_normal_and_later_input_continues()
    {
        var store = new InMemoryStorageStore
        {
            FailWith = () => new IOException("temporarily unavailable")
        };
        await using var node = new FlowContentStorageGetNode(
            store,
            new StorageGetOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new StorageGetRequest { Key = "a" }));
        var failure = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        failure.Error.ShouldNotBeNull().Code.ShouldBe(StorageErrorCodeNames.GetFailed);
        failure.Error.IsTransient.ShouldBeTrue();

        store.FailWith = null;
        await node.Input.SendAsync(FlowMessage.Create(new StorageGetRequest { Key = "a" }));
        var missing = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        missing.Kind.ShouldBe(StorageResultKinds.GetNotFound);
    }

    [Fact]
    public async Task Completion_drains_accepted_results_and_events()
    {
        var store = new InMemoryStorageStore();
        await using var node = new FlowContentStoragePutNode(
            store,
            new StoragePutOptions { Collection = "items" });
        var output = StorageTestSink.Link(node.Output);
        var events = StorageTestSink.Link(node.Events);

        foreach (var key in new[] { "a", "b" })
        {
            (await node.Input.SendAsync(FlowMessage.Create(new StorageContentPutRequest
            {
                Key = key,
                Content = FlowContent.FromBytes(Encoding.UTF8.GetBytes(key))
            }))).ShouldBeTrue();
        }

        node.Complete();
        await node.Completion.WaitAsync(Timeout);

        (await StorageTestSink.DrainUntilCompletedAsync(output)).Count.ShouldBe(2);
        (await StorageTestSink.DrainUntilCompletedAsync(events)).Count.ShouldBe(2);
    }
}
