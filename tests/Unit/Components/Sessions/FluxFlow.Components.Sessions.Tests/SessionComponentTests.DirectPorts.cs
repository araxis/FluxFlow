using System.Threading.Tasks.Dataflow;
using FluxFlow.Core;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Nodes;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Sessions.Tests;

public sealed partial class SessionComponentTests
{
    [Fact]
    public async Task Direct_session_graph_records_queries_and_finalizes_without_owning_store()
    {
        await using var store = new TestSessionStore();
        await using var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("sessions");
        var recorder = new SessionRecorderNode(
            new SessionRecorderOptions { SessionId = "direct-session", SessionName = "direct" }, store);
        var query = new SessionQueryNode(new SessionQueryOptions(), store);
        AssertDirectPorts(recorder, recorder.Input, recorder.Output, recorder.Events);
        AssertDirectPorts(query, query.Input, query.Output, query.Events);
        var recordPort = workflow.Add("record", recorder);
        var queryPort = workflow.Add("query", query);
        var sink = new DirectSink();
        var target = workflow.Add("sink", sink);
        recordPort.Output.LinkTo(target.Input);
        queryPort.Output.LinkTo(target.Input);
        await app.StartAsync();
        var error = FlowMessage.CreateError(new FlowError("upstream.failed", "Expected upstream error.", "upstream"));
        foreach (var component in new[] { recordPort, queryPort })
        {
            (await app.SendAsync(component.Input, error)).ShouldBeTrue();
            (await sink.ReceiveAsync()).ShouldBeSameAs(error);
        }
        store.Records.ShouldBeEmpty();
        store.Metadata.ShouldBeNull();
        var input = SessionRequest(ContentInput(7, "direct-record"));
        (await app.SendAsync(recordPort.Input, input)).ShouldBeTrue();
        var recorded = await sink.ReceiveAsync();
        recorded.IsError.ShouldBeFalse();
        var record = recorded.Value.Deserialize<SessionContentRecord>()!;
        record.SessionId.ShouldBe("direct-session");
        record.Sequence.ShouldBe(1);
        record.Content.Bytes.ToArray().ShouldBe(new byte[] { 7 });
        recorded.CorrelationId.ShouldBe(input.CorrelationId);
        recorded.TraceId.ShouldBe(input.TraceId);
        recorded.CausationId.ShouldBe(input.MessageId);
        store.Records.ShouldHaveSingleItem().Name.ShouldBe("direct-record");

        var request = SessionRequest(new SessionQueryRequest { Name = "direct" });
        (await app.SendAsync(queryPort.Input, request)).ShouldBeTrue();
        var result = await sink.ReceiveAsync();
        result.IsError.ShouldBeFalse();
        var outcome = result.Value.Deserialize<SessionQueryOutcome>()!;
        outcome.Count.ShouldBe(1);
        outcome.Sessions.ShouldHaveSingleItem().SessionId.ShouldBe("direct-session");
        result.CausationId.ShouldBe(request.MessageId);
        result.TraceId.ShouldBe(request.TraceId);
        result.CorrelationId.ShouldBe(request.CorrelationId);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await recorder.SessionCompleted.WaitAsync(TimeSpan.FromSeconds(10));
        await app.DisposeAsync();

        recorder.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        query.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        store.Metadata.ShouldNotBeNull().MessageCount.ShouldBe(1);
        store.Metadata.EndedAt.ShouldNotBeNull();
        store.DisposeCalls.ShouldBe(0);
        (await store.GetSessionAsync("direct-session")).ShouldNotBeNull().EndedAt.ShouldNotBeNull();
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
