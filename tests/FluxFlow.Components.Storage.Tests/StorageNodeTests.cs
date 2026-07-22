using System.Text;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Storage.Tests;

public sealed class StorageNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Put_and_get_preserve_exact_content_metadata_and_lineage()
    {
        var store = new InMemoryStorageStore();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-20T14:00:00Z"));
        var attributes = new Dictionary<string, string> { ["tenant"] = "north" };
        byte[] bytes = [0x00, 0x7F, 0xFF];
        await using var put = new StoragePutNode(
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

        await using var get = new StorageGetNode(
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
        await using var node = new StoragePutNode(
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
        await using var put = new StoragePutNode(
            store,
            new StoragePutOptions { Collection = "items" });
        await using var get = new StorageGetNode(
            store,
            new StorageGetOptions { Collection = "items" });
        await using var query = new StorageQueryNode(
            store,
            new StorageQueryOptions { Collection = "items" });
        await using var delete = new StorageDeleteNode(
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
        await using var node = new StorageGetNode(
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
        await using (var put = new StoragePutNode(
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

        await using var query = new StorageQueryNode(
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
        await using (var put = new StoragePutNode(
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

        await using var delete = new StorageDeleteNode(
            store,
            new StorageDeleteOptions { Collection = "items" });
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
        await using var node = new StorageGetNode(
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
        await using var node = new StoragePutNode(
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

    [Fact]
    public async Task Put_preserves_request_mode_override_and_record_suppression()
    {
        var store = new InMemoryStorageStore();
        await using var node = new StoragePutNode(
            store,
            new StoragePutOptions
            {
                Collection = "items",
                Mode = StorageWriteMode.Create,
                EmitStoredRecord = false
            });
        var output = StorageTestSink.Link(node.Output);

        foreach (var (text, mode) in new[]
                 {
                     ("one", StorageWriteMode.Upsert),
                     ("two", StorageWriteMode.Replace)
                 })
        {
            await node.Input.SendAsync(FlowMessage.Create(new StorageContentPutRequest
            {
                Key = "a",
                Content = FlowContent.FromBytes(Encoding.UTF8.GetBytes(text), "text/plain"),
                Mode = mode
            }));
        }

        var created = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        created.Value.ShouldNotBeNull().Version.ShouldBe(1);
        created.Value.Record.ShouldBeNull();
        var replaced = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        replaced.Value.ShouldNotBeNull().Version.ShouldBe(2);
        replaced.Value.Record.ShouldBeNull();
    }

    [Fact]
    public async Task Query_honors_filter_paging_and_result_suppression()
    {
        var store = new InMemoryStorageStore();
        await using (var put = new StoragePutNode(
                         store,
                         new StoragePutOptions { Collection = "items" }))
        {
            var output = StorageTestSink.Link(put.Output);
            foreach (var key in new[] { "order:1", "user:1", "user:2" })
            {
                await put.Input.SendAsync(FlowMessage.Create(new StorageContentPutRequest
                {
                    Key = key,
                    Content = FlowContent.FromBytes(Encoding.UTF8.GetBytes(key))
                }));
                (await output.ReceiveAsync().WaitAsync(Timeout)).Payload.IsError.ShouldBeFalse();
            }
        }

        await using var query = new StorageQueryNode(
            store,
            new StorageQueryOptions
            {
                Collection = "items",
                EmitRecordsInResult = false
            });
        var results = StorageTestSink.Link(query.Output);
        await query.Input.SendAsync(FlowMessage.Create(new StorageQueryRequest
        {
            KeyPrefix = "user:",
            Offset = 1,
            Limit = 1
        }));

        var result = (await results.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.Kind.ShouldBe(StorageResultKinds.QueryCompleted);
        result.Value.ShouldNotBeNull().Count.ShouldBe(1);
        result.Value.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task Store_contract_anomalies_are_normal_failure_results()
    {
        var store = new NullResultStorageStore();
        await using var put = new StoragePutNode(
            store,
            new StoragePutOptions { Collection = "items" });
        await using var query = new StorageQueryNode(
            store,
            new StorageQueryOptions { Collection = "items" });
        await using var delete = new StorageDeleteNode(
            store,
            new StorageDeleteOptions { Collection = "items" });
        var putOutput = StorageTestSink.Link(put.Output);
        var queryOutput = StorageTestSink.Link(query.Output);
        var deleteOutput = StorageTestSink.Link(delete.Output);

        await put.Input.SendAsync(FlowMessage.Create(new StorageContentPutRequest
        {
            Key = "a",
            Content = FlowContent.FromBytes(new byte[] { 1 })
        }));
        await query.Input.SendAsync(FlowMessage.Create(new StorageQueryRequest()));
        await delete.Input.SendAsync(FlowMessage.Create(new StorageDeleteRequest { Key = "a" }));

        (await putOutput.ReceiveAsync().WaitAsync(Timeout)).Payload.Error
            .ShouldNotBeNull().Code.ShouldBe(StorageErrorCodeNames.PutFailed);
        (await queryOutput.ReceiveAsync().WaitAsync(Timeout)).Payload.Error
            .ShouldNotBeNull().Code.ShouldBe(StorageErrorCodeNames.QueryFailed);
        (await deleteOutput.ReceiveAsync().WaitAsync(Timeout)).Payload.Error
            .ShouldNotBeNull().Code.ShouldBe(StorageErrorCodeNames.DeleteFailed);
        put.Completion.IsFaulted.ShouldBeFalse();
        query.Completion.IsFaulted.ShouldBeFalse();
        delete.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Store_identity_and_query_contract_violations_are_normal_failures()
    {
        var store = new InvalidResultStorageStore();
        await using var get = new StorageGetNode(
            store,
            new StorageGetOptions { Collection = "items" });
        await using var query = new StorageQueryNode(
            store,
            new StorageQueryOptions { Collection = "items" });
        var getOutput = StorageTestSink.Link(get.Output);
        var queryOutput = StorageTestSink.Link(query.Output);

        await get.Input.SendAsync(FlowMessage.Create(new StorageGetRequest { Key = "a" }));
        await query.Input.SendAsync(FlowMessage.Create(new StorageQueryRequest
        {
            KeyPrefix = "user:",
            Limit = 1
        }));

        (await getOutput.ReceiveAsync().WaitAsync(Timeout)).Payload.Error
            .ShouldNotBeNull().Code.ShouldBe(StorageErrorCodeNames.StoredContentInvalid);
        (await queryOutput.ReceiveAsync().WaitAsync(Timeout)).Payload.Error
            .ShouldNotBeNull().Code.ShouldBe(StorageErrorCodeNames.QueryFailed);
    }

    [Fact]
    public async Task Output_fans_out_and_events_preserve_result_order()
    {
        var store = new InMemoryStorageStore();
        await using var node = new StoragePutNode(
            store,
            new StoragePutOptions { Collection = "items" });
        var first = StorageTestSink.Link(node.Output);
        var second = StorageTestSink.Link(node.Output);
        var events = StorageTestSink.Link(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(new StorageContentPutRequest
        {
            Key = "bad",
            Content = FlowContent.FromValue(FlowValue.From("serialize upstream"))
        }));
        await node.Input.SendAsync(FlowMessage.Create(new StorageContentPutRequest
        {
            Key = "good",
            Content = FlowContent.FromBytes(new byte[] { 1 })
        }));
        node.Complete();
        await node.Completion.WaitAsync(Timeout);

        var firstResults = await StorageTestSink.DrainUntilCompletedAsync(first);
        var secondResults = await StorageTestSink.DrainUntilCompletedAsync(second);
        firstResults.Select(item => item.Payload.Kind).ShouldBe([
            StorageResultKinds.PutFailed,
            StorageResultKinds.PutStored
        ]);
        secondResults.Select(item => item.Payload.Kind).ShouldBe([
            StorageResultKinds.PutFailed,
            StorageResultKinds.PutStored
        ]);
        (await StorageTestSink.DrainUntilCompletedAsync(events))
            .Select(item => item.Name)
            .ShouldBe([
                StorageDiagnosticNames.PutFailed,
                StorageDiagnosticNames.PutStored
            ]);
    }

    [Fact]
    public void Nodes_reject_null_store()
    {
        Should.Throw<ArgumentNullException>(() => new StoragePutNode(null!));
        Should.Throw<ArgumentNullException>(() => new StorageGetNode(null!));
        Should.Throw<ArgumentNullException>(() => new StorageQueryNode(null!));
        Should.Throw<ArgumentNullException>(() => new StorageDeleteNode(null!));
    }

    private sealed class NullResultStorageStore : IStorageStore
    {
        public Task<StorageRecord> PutAsync(
            StoragePutRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult<StorageRecord>(null!);

        public Task<StorageRecord?> GetAsync(
            StorageGetRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult<StorageRecord?>(null);

        public Task<IReadOnlyList<StorageRecord>> QueryAsync(
            StorageQueryRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StorageRecord>>(null!);

        public Task<StorageResult> DeleteAsync(
            StorageDeleteRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult<StorageResult>(null!);
    }

    private sealed class InvalidResultStorageStore : IStorageStore
    {
        public Task<StorageRecord> PutAsync(
            StoragePutRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StorageRecord?> GetAsync(
            StorageGetRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult<StorageRecord?>(new StorageRecord
            {
                Collection = "other",
                Key = request.Key!,
                StoredAt = DateTimeOffset.UtcNow
            });

        public Task<IReadOnlyList<StorageRecord>> QueryAsync(
            StorageQueryRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StorageRecord>>([
                new StorageRecord
                {
                    Collection = "items",
                    Key = "user:1",
                    StoredAt = DateTimeOffset.UtcNow
                },
                new StorageRecord
                {
                    Collection = "items",
                    Key = "user:2",
                    StoredAt = DateTimeOffset.UtcNow
                }
            ]);

        public Task<StorageResult> DeleteAsync(
            StorageDeleteRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
