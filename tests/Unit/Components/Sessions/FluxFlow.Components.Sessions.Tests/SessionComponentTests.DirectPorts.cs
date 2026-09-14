using System.Text.Json;
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
    [Theory]
    [InlineData(false, "null")]
    [InlineData(false, "42")]
    [InlineData(true, "null")]
    [InlineData(true, "42")]
    public async Task Local_request_parsing_preserves_errors_and_does_not_start_a_session(bool record, string json)
    {
        await using var store = new TestSessionStore();
        await using IFlowComponent node = record
            ? new SessionRecorderNode(new SessionRecorderOptions(), store)
            : new SessionQueryNode(new SessionQueryOptions(), store);
        var output = new BufferBlock<FlowMessage>();
        var events = new BufferBlock<FlowMessage>();
        using var outputLink = node.Ports.Outputs["Output"].LinkTo(output);
        using var eventLink = node.Ports.Events!.LinkTo(events);
        if (!record)
        {
            var started = (await events.ReceiveAsync(TimeSpan.FromSeconds(5))).ToFlowEvent();
            started.Name.ShouldBe("session.query.started");
        }
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var input = FlowMessage.Create((FlowValue)new JsonFlowValue(document.RootElement), correlationId: CorrelationId.New());
        (await node.Ports.Inputs["Input"].SendAsync(input)).ShouldBeTrue();
        var result = await output.ReceiveAsync(TimeSpan.FromSeconds(5));
        result.Error!.Code.ShouldBe(record ? "sessions.record.invalid_shape" : "sessions.query.invalid_shape");
        result.Error.Category.ShouldBe("Sessions");
        result.Error.Details!.Value.GetProperty("target").GetString()
            .ShouldBe(record ? nameof(SessionContentRecordInput) : nameof(SessionQueryRequest));
        result.Error.Details.Value.GetProperty("reason").GetString().ShouldNotBeNullOrWhiteSpace();
        result.CausationId.ShouldBe(input.MessageId);
        var diagnostic = (await events.ReceiveAsync(TimeSpan.FromSeconds(5))).ToFlowEvent();
        diagnostic.Name.ShouldBe(record ? "session.recorder.rejected" : "session.query.rejected");
        diagnostic.CorrelationId.ShouldBe(input.CorrelationId);
        diagnostic.Attributes["errorCode"].ShouldBe(result.Error.Code);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        store.Metadata.ShouldBeNull();
        store.Records.ShouldBeEmpty();
    }

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
        var input = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(ContentInput(7, "direct-record"), new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            correlationId: CorrelationId.New(),
            headers: new Dictionary<string, string> { ["tenant"] = "north" });
        (await app.SendAsync(recordPort.Input, input)).ShouldBeTrue();
        var recorded = await sink.ReceiveAsync();
        recorded.IsError.ShouldBeFalse();
        var record = recorded.Value.Json.Deserialize<SessionContentRecord>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        record.SessionId.ShouldBe("direct-session");
        record.Sequence.ShouldBe(1);
        record.Content.Bytes.ToArray().ShouldBe(new byte[] { 7 });
        recorded.CorrelationId.ShouldBe(input.CorrelationId);
        recorded.TraceId.ShouldBe(input.TraceId);
        recorded.CausationId.ShouldBe(input.MessageId);
        recorded.MessageId.ShouldNotBe(input.MessageId);
        recorded.Headers["tenant"].ShouldBe("north");
        store.Records.ShouldHaveSingleItem().Name.ShouldBe("direct-record");

        var request = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new SessionQueryRequest { Name = "direct" }, new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            correlationId: input.CorrelationId, traceId: input.TraceId, headers: input.Headers);
        (await app.SendAsync(queryPort.Input, request)).ShouldBeTrue();
        var result = await sink.ReceiveAsync();
        result.IsError.ShouldBeFalse();
        var outcome = result.Value.Json.Deserialize<SessionQueryOutcome>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        outcome.Count.ShouldBe(1);
        outcome.Sessions.ShouldHaveSingleItem().SessionId.ShouldBe("direct-session");
        result.CausationId.ShouldBe(request.MessageId);
        result.TraceId.ShouldBe(request.TraceId);
        result.CorrelationId.ShouldBe(request.CorrelationId);
        result.MessageId.ShouldNotBe(request.MessageId);
        result.Headers["tenant"].ShouldBe("north");
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
