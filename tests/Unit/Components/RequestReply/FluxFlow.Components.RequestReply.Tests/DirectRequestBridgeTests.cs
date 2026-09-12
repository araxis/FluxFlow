using System.Threading.Tasks.Dataflow;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.RequestReply.Tests;

public sealed class DirectRequestBridgeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Direct_bridge_waits_for_start_and_drains_responses_on_stop()
    {
        await using var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("requests");
        var bridge = new RequestReplyCoordinator<string, string>();
        bridge.Ports.Inputs.ShouldBeEmpty();
        bridge.Ports.Controls.ShouldHaveSingleItem().Key.ShouldBe("Responses");
        bridge.Ports.Controls["Responses"].ShouldBeSameAs(bridge.Responses);
        bridge.Ports.Outputs.ShouldHaveSingleItem().Key.ShouldBe("Output");
        bridge.Ports.Outputs["Output"].ShouldBeSameAs(bridge.Output);
        bridge.Ports.Events.ShouldBeSameAs(bridge.Events);
        var entered = new TaskCompletionSource<FlowMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new ReplyNode(async request =>
        {
            entered.TrySetResult(request);
            await release.Task;
            return request.With(FlowValue.From("reply:" + request.Value!.Deserialize<string>()));
        });
        var source = workflow.Add("bridge", bridge);
        var handler = workflow.Add("handler", worker);
        source.Output.LinkTo(handler.Input);
        handler.Output.LinkTo(source.Inputs["Responses"]);
        var context = new Context<string, string>("hello");
        (await bridge.Incoming.SendAsync(context)).ShouldBeTrue();
        context.Reads.ShouldBe(0);
        bridge.InFlightCount.ShouldBe(0);

        await app.StartAsync();
        var request = await entered.Task.WaitAsync(Deadline);
        request.CorrelationId.ShouldNotBeNull();
        request.CorrelationId.ShouldNotBe(context.CorrelationId);
        request.Value!.Deserialize<string>().ShouldBe("hello");
        var stopping = app.StopAsync();
        try
        {
            await bridge.ProductionCompletion.WaitAsync(Deadline);
            stopping.IsCompleted.ShouldBeFalse();
            bridge.Responses.Completion.IsCompleted.ShouldBeFalse();
            context.Settled.Task.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        await stopping.WaitAsync(Deadline);
        context.Response.ShouldBe("reply:hello");
        context.ReplyCalls.ShouldBe(1);
        context.FailCalls.ShouldBe(0);
        context.AckCalls.ShouldBe(0);
        bridge.InFlightCount.ShouldBe(0);
        bridge.Responses.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Direct_bridge_materializes_binary_reply_once()
    {
        var conversions = 0;
        var bridge = new RequestReplyCoordinator<FlowContent, FlowContent>(materializeResponse: message =>
        {
            Interlocked.Increment(ref conversions);
            return message.ConvertValue(message.Value!.Deserialize<FlowContent>()!);
        });
        await using var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("binary");
        var source = workflow.Add("bridge", bridge);
        var handler = workflow.Add("echo", new ReplyNode(request => Task.FromResult(request.With(request.Value!))));
        source.Output.LinkTo(handler.Input);
        handler.Output.LinkTo(source.Inputs["Responses"]);
        byte[] bytes = [0, 127, 255, 0];
        var context = new Context<FlowContent, FlowContent>(FlowContent.FromBytes(bytes, "application/octet-stream"));
        await app.StartAsync();
        (await bridge.Incoming.SendAsync(context)).ShouldBeTrue();
        await context.Settled.Task.WaitAsync(Deadline);
        await app.StopAsync().WaitAsync(Deadline);
        context.Response.ShouldNotBeNull();
        context.Response.Bytes.ToArray().ShouldBe(bytes);
        context.Response.ContentType.ShouldBe("application/octet-stream");
        context.ReplyCalls.ShouldBe(1);
        context.FailCalls.ShouldBe(0);
        conversions.ShouldBe(1);
        bridge.InFlightCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bridge_pre_start_shutdown_settles_every_admitted_context(bool fault)
    {
        await using var bridge = new RequestReplyCoordinator<string, string>(new RequestReplyOptions { Capacity = 3 });
        var contexts = new[] { new Context<string, string>("one"), new Context<string, string>("two") };
        foreach (var context in contexts) (await bridge.Incoming.SendAsync(context)).ShouldBeTrue();
        var failure = new InvalidOperationException("stopped before start");
        if (fault) bridge.Fault(failure); else bridge.Complete();
        var error = await Record.ExceptionAsync(() => bridge.Completion.WaitAsync(Deadline));
        if (fault) error.ShouldBeSameAs(failure); else error.ShouldBeNull();
        foreach (var context in contexts)
        {
            context.Reads.ShouldBe(0);
            context.FailCalls.ShouldBe(1);
            context.ReplyCalls.ShouldBe(0);
            context.AckCalls.ShouldBe(0);
            context.Settled.Task.IsCompletedSuccessfully.ShouldBeTrue();
            if (fault) context.Error.ShouldBeSameAs(failure);
            else context.Error.ShouldBeOfType<OperationCanceledException>();
        }
        bridge.InFlightCount.ShouldBe(0);
        bridge.Incoming.Completion.IsCompleted.ShouldBeTrue();
        bridge.Output.Completion.IsCompleted.ShouldBeTrue();
        bridge.Responses.Completion.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Bridge_precanceled_start_preserves_queued_request()
    {
        await using var bridge = new RequestReplyCoordinator<string, string>();
        var context = new Context<string, string>("request");
        (await bridge.Incoming.SendAsync(context)).ShouldBeTrue();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => bridge.StartAsync(canceled.Token));
        context.Reads.ShouldBe(0);
        context.Settled.Task.IsCompleted.ShouldBeFalse();
        await bridge.StartAsync();
        var request = await bridge.Output.ReceiveAsync().WaitAsync(Deadline);
        (await bridge.Responses.SendAsync(request.With(FlowValue.From("reply")))).ShouldBeTrue();
        await context.Settled.Task.WaitAsync(Deadline);
        context.Response.ShouldBe("reply");
        context.ReplyCalls.ShouldBe(1);
        context.FailCalls.ShouldBe(0);
        bridge.InFlightCount.ShouldBe(0);
    }

    [Fact]
    public async Task Direct_bridge_invalid_reply_fails_only_its_caller()
    {
        var bridge = new RequestReplyCoordinator<string, string>();
        await using var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("requests");
        var source = workflow.Add("bridge", bridge);
        var handler = workflow.Add("handler", new ReplyNode(request => Task.FromResult(
            request.With(request.Value!.Deserialize<string>() == "invalid" ? FlowValue.From(42) : FlowValue.From("ok")))));
        source.Output.LinkTo(handler.Input);
        handler.Output.LinkTo(source.Inputs["Responses"]);
        await app.StartAsync();
        var invalid = new Context<string, string>("invalid");
        (await bridge.Incoming.SendAsync(invalid)).ShouldBeTrue();
        await invalid.Settled.Task.WaitAsync(Deadline);
        invalid.FailCalls.ShouldBe(1);
        invalid.ReplyCalls.ShouldBe(0);
        invalid.Error.ShouldNotBeNull();
        invalid.Error.Message.ShouldStartWith("request.reply.invalid_shape:");
        var valid = new Context<string, string>("valid");
        (await bridge.Incoming.SendAsync(valid)).ShouldBeTrue();
        await valid.Settled.Task.WaitAsync(Deadline);
        await app.StopAsync().WaitAsync(Deadline);
        valid.Response.ShouldBe("ok");
        valid.ReplyCalls.ShouldBe(1);
        valid.FailCalls.ShouldBe(0);
        bridge.InFlightCount.ShouldBe(0);
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    private sealed class ReplyNode(Func<FlowMessage, Task<FlowMessage>> reply) : FlowNode(
        new FlowNodeOptions { InputCapacity = 1, OutputCapacity = 1 })
    {
        protected override async Task ProcessAsync(FlowMessage message) => await EmitAsync(await reply(message), Stopping);
    }

    private sealed class Context<TRequest, TResponse>(TRequest request) : IRequestContext<TRequest, TResponse>
    {
        private int _reads;
        public int Reads => Volatile.Read(ref _reads);
        public TRequest Request { get { Interlocked.Increment(ref _reads); return request; } }
        public CorrelationId? CorrelationId { get; } = Nodes.CorrelationId.New();
        public TaskCompletionSource Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TResponse? Response { get; private set; }
        public Exception? Error { get; private set; }
        public int ReplyCalls { get; private set; }
        public int FailCalls { get; private set; }
        public int AckCalls { get; private set; }
        public Task ReplyAsync(TResponse response, CancellationToken cancellationToken = default)
        { Response = response; ReplyCalls++; Settled.TrySetResult(); return Task.CompletedTask; }
        public Task FailAsync(Exception error, CancellationToken cancellationToken = default)
        { Error = error; FailCalls++; Settled.TrySetResult(); return Task.CompletedTask; }
        public Task AcknowledgeAsync(CancellationToken cancellationToken = default)
        { AckCalls++; Settled.TrySetResult(); return Task.CompletedTask; }
    }
}
