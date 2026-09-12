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
            Key = "one", Content = FlowContent.FromBytes(bytes, "application/octet-stream")
        });
        written.Value.ShouldNotBeNull().Deserialize<StoragePutOutcome>()!.Record.ShouldNotBeNull()
            .Content.Bytes.ToArray().ShouldBe(bytes);
        store.RecordCount.ShouldBe(1);
        var read = await SendAsync(getPort, new StorageGetRequest { Key = "one" });
        var record = read.Value.ShouldNotBeNull().Deserialize<StorageGetOutcome>()!;
        record.Found.ShouldBeTrue();
        record.Record.ShouldNotBeNull().Content.Bytes.ToArray().ShouldBe(bytes);
        var found = await SendAsync(queryPort, new StorageQueryRequest { KeyPrefix = "one" });
        var queryResult = found.Value.ShouldNotBeNull().Deserialize<StorageQueryOutcome>()!;
        queryResult.Count.ShouldBe(1);
        var foundRecord = queryResult.Records.ShouldHaveSingleItem();
        foundRecord.Key.ShouldBe("one");
        foundRecord.Content.Bytes.ToArray().ShouldBe(bytes);
        var removed = await SendAsync(deletePort, new StorageDeleteRequest { Key = "one" });
        removed.Value.ShouldNotBeNull().Deserialize<StorageDeleteOutcome>()!.Deleted.ShouldBeTrue();
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
            var input = FlowMessage.Create(FlowValue.From(request),
                correlationId: new CorrelationId("storage"), traceId: new TraceId("storage-trace"));
            (await app.SendAsync(component.Input, input)).ShouldBeTrue();
            var result = await sink.ReceiveAsync();
            result.IsError.ShouldBeFalse();
            result.CorrelationId.ShouldBe(input.CorrelationId);
            result.TraceId.ShouldBe(input.TraceId);
            result.CausationId.ShouldBe(input.MessageId);
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
