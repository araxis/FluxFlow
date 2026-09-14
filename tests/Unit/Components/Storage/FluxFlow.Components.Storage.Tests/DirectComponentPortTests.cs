using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Storage.Tests;

public sealed class DirectComponentPortTests
{
    [Theory]
    [InlineData("get", "null")]
    [InlineData("get", "42")]
    [InlineData("put", "null")]
    [InlineData("put", "42")]
    [InlineData("query", "null")]
    [InlineData("query", "42")]
    [InlineData("delete", "null")]
    [InlineData("delete", "42")]
    public async Task Local_deserialization_preserves_domain_rejections(string operation, string json)
    {
        await using var store = new InMemoryStorageStore();
        await using IFlowComponent node = operation switch
        {
            "get" => new StorageGetNode(store),
            "put" => new StoragePutNode(store),
            "query" => new StorageQueryNode(store),
            _ => new StorageDeleteNode(store)
        };
        var output = new BufferBlock<FlowMessage>();
        var events = new BufferBlock<FlowMessage>();
        using var outputLink = node.Ports.Outputs["Output"].LinkTo(output);
        using var eventLink = node.Ports.Events!.LinkTo(events);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var input = FlowMessage.Create((FlowValue)new JsonFlowValue(document.RootElement), correlationId: CorrelationId.New());
        (await node.Ports.Inputs["Input"].SendAsync(input)).ShouldBeTrue();
        var result = await output.ReceiveAsync(TimeSpan.FromSeconds(5));
        result.Error!.Code.ShouldBe($"storage.{operation}.invalid_shape");
        result.Error.Category.ShouldBe("Storage");
        result.Error.Details!.Value.GetProperty("target").GetString().ShouldNotBeNullOrWhiteSpace();
        result.Error.Details.Value.GetProperty("reason").GetString().ShouldNotBeNullOrWhiteSpace();
        result.CausationId.ShouldBe(input.MessageId);
        var diagnostic = (await events.ReceiveAsync(TimeSpan.FromSeconds(5))).ToFlowEvent();
        diagnostic.Name.ShouldBe($"storage.{operation}.rejected");
        diagnostic.CorrelationId.ShouldBe(input.CorrelationId);
        diagnostic.Attributes["errorCode"].ShouldBe(result.Error.Code);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Direct_storage_graph_processes_all_operations_without_owning_store()
    {
        await using var store = new InMemoryStorageStore();
        await using var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("storage");
        var put = new StoragePutNode(store, new StoragePutOptions { Collection = "items" });
        var get = new StorageGetNode(store, new StorageGetOptions { Collection = "items" });
        var query = new StorageQueryNode(store, new StorageQueryOptions { Collection = "items" });
        var delete = new StorageDeleteNode(store, new StorageDeleteOptions { Collection = "items" });
        AssertDirectPorts(put, put.Input, put.Output, put.Events);
        AssertDirectPorts(get, get.Input, get.Output, get.Events);
        AssertDirectPorts(query, query.Input, query.Output, query.Events);
        AssertDirectPorts(delete, delete.Input, delete.Output, delete.Events);
        var putPort = workflow.Add("put", put);
        var getPort = workflow.Add("get", get);
        var queryPort = workflow.Add("query", query);
        var deletePort = workflow.Add("delete", delete);
        var sink = new DirectSink();
        var target = workflow.Add("sink", sink);
        var components = new[] { putPort, getPort, queryPort, deletePort };
        foreach (var component in components)
            component.Output.LinkTo(target.Input);
        await app.StartAsync();
        var error = FlowMessage.CreateError(new FlowError("upstream.failed", "Expected upstream error.", "upstream"));
        foreach (var component in components)
        {
            (await app.SendAsync(component.Input, error)).ShouldBeTrue();
            (await sink.ReceiveAsync()).ShouldBeSameAs(error);
        }
        store.RecordCount.ShouldBe(0);
        byte[] bytes = [0, 127, 255];
        var written = await SendAsync(putPort, new StorageContentPutRequest
        {
            Key = "one",
            Content = FlowContent.FromBytes(bytes, "application/octet-stream")
        });
        written.Value.ShouldNotBeNull().Json.Deserialize<StoragePutOutcome>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Record.ShouldNotBeNull()
            .Content.Bytes.ToArray().ShouldBe(bytes);
        store.RecordCount.ShouldBe(1);
        var read = await SendAsync(getPort, new StorageGetRequest { Key = "one" });
        var record = read.Value.ShouldNotBeNull().Json.Deserialize<StorageGetOutcome>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        record.Found.ShouldBeTrue();
        record.Record.ShouldNotBeNull().Content.Bytes.ToArray().ShouldBe(bytes);
        var found = await SendAsync(queryPort, new StorageQueryRequest { KeyPrefix = "one" });
        var queryResult = found.Value.ShouldNotBeNull().Json.Deserialize<StorageQueryOutcome>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        queryResult.Count.ShouldBe(1);
        var foundRecord = queryResult.Records.ShouldHaveSingleItem();
        foundRecord.Key.ShouldBe("one");
        foundRecord.Content.Bytes.ToArray().ShouldBe(bytes);
        var removed = await SendAsync(deletePort, new StorageDeleteRequest { Key = "one" });
        removed.Value.ShouldNotBeNull().Json.Deserialize<StorageDeleteOutcome>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Deleted.ShouldBeTrue();
        store.RecordCount.ShouldBe(0);

        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await app.DisposeAsync();
        foreach (var component in components)
            component.Instance.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        store.DisposeCalls.ShouldBe(0);
        (await store.GetAsync(new StorageGetRequest { Collection = "items", Key = "one" })).ShouldBeNull();

        async Task<FlowMessage> SendAsync(FlowComponent component, object request)
        {
            var input = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(request, new JsonSerializerOptions(JsonSerializerDefaults.Web))),
                correlationId: new CorrelationId("storage"), traceId: new TraceId("storage-trace"),
                headers: new Dictionary<string, string> { ["tenant"] = "north" });
            (await app.SendAsync(component.Input, input)).ShouldBeTrue();
            var result = await sink.ReceiveAsync();
            result.IsError.ShouldBeFalse();
            result.CorrelationId.ShouldBe(input.CorrelationId);
            result.TraceId.ShouldBe(input.TraceId);
            result.CausationId.ShouldBe(input.MessageId);
            result.MessageId.ShouldNotBe(input.MessageId);
            result.Headers["tenant"].ShouldBe("north");
            return result;
        }
    }

    private static void AssertDirectPorts(IFlowComponent component, ITargetBlock<FlowMessage> input,
        ISourceBlock<FlowMessage> output, ISourceBlock<FlowMessage> events)
    {
        component.Ports.ShouldBeSameAs(component.Ports);
        component.Ports.Inputs.ShouldHaveSingleItem().Key.ShouldBe("Input");
        component.Ports.Outputs.ShouldHaveSingleItem().Key.ShouldBe("Output");
        component.Ports.Inputs["Input"].ShouldBeSameAs(input);
        component.Ports.Outputs["Output"].ShouldBeSameAs(output);
        component.Ports.Events.ShouldBeSameAs(events);
        component.Ports.Controls.ShouldBeEmpty();
    }

    private sealed class DirectSink : IFlowComponent
    {
        private readonly BufferBlock<FlowMessage> _input = new(
            new DataflowBlockOptions { BoundedCapacity = 2 });

        public DirectSink() => Ports = new FlowPorts(
            inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = _input });

        public FlowPorts Ports { get; }
        public Task Completion => _input.Completion;
        public Task<FlowMessage> ReceiveAsync() => _input.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));
        public void Complete() => _input.Complete();
        public void Fault(Exception exception) => ((IDataflowBlock)_input).Fault(exception);
        public async ValueTask DisposeAsync() { Complete(); await Completion; }
    }
}
