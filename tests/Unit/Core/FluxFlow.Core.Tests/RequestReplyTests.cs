using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class RequestReplyTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Concurrent_requests_with_one_trace_receive_only_their_own_first_reply()
    {
        await using var app = new FlowApplication();
        var capture = new Capture();
        var component = app.AddWorkflow("main").Add("request", capture);
        app.RepliesFrom(component.Output);
        await app.StartAsync();
        var trace = TraceId.New();
        var first = app.RequestAsync(component.Input, component.Output, Request(1, trace), Deadline);
        var second = app.RequestAsync(component.Input, component.Output, Request(2, trace), Deadline);
        var firstSent = await capture.Requests.ReceiveAsync(Deadline);
        var secondSent = await capture.Requests.ReceiveAsync(Deadline);
        firstSent.TraceId.ShouldBe(trace);
        secondSent.TraceId.ShouldBe(trace);
        firstSent.CorrelationId.ShouldNotBe(secondSent.CorrelationId);
        firstSent.CorrelationId.ShouldNotBeNull();
        var secondReply = secondSent.With(FlowValue.From(20));
        await capture.PublishAsync(secondReply);
        (await second.WaitAsync(Deadline)).ShouldBeSameAs(secondReply);
        first.IsCompleted.ShouldBeFalse();
        app.PendingRequestCount.ShouldBe(1);
        await capture.PublishAsync(secondReply);
        var firstReply = firstSent.With(FlowValue.From(10));
        await capture.PublishAsync(firstReply);
        (await first.WaitAsync(Deadline)).ShouldBeSameAs(firstReply);
        app.PendingRequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Fast_reply_is_registered_before_admission_and_preserves_payload_ownership()
    {
        await using var app = new FlowApplication();
        var component = app.AddWorkflow("main").Add("echo", new Echo());
        app.RepliesFrom(component.Output);
        await app.StartAsync();
        var original = Request(7);
        var response = await app.RequestAsync(component.Input, component.Output, original, Deadline);
        response.Value.ShouldBeSameAs(original.Value);
        response.Headers.ShouldBeSameAs(original.Headers);
        response.MessageId.ShouldBe(original.MessageId);
        response.CorrelationId.ShouldNotBe(original.CorrelationId);
        response.TraceId.ShouldBe(original.TraceId);
        app.PendingRequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Wrong_reply_route_cannot_settle_an_exchange()
    {
        await using var app = new FlowApplication();
        var main = app.AddWorkflow("main");
        var capture = new Capture();
        var wrong = new Capture();
        var observed = new Echo();
        var request = main.Add("request", capture);
        var wrongPort = main.Add("wrong", wrong);
        var observer = main.Add("observed", observed);
        var observedReply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wrongPort.Output.LinkTo(observer.Input);
        observer.Output.LinkTo(main.Add("discard", new Discard(observedReply)).Input);
        app.RepliesFrom(request.Output);
        app.RepliesFrom(wrongPort.Output);
        await app.StartAsync();
        var pending = app.RequestAsync(request.Input, request.Output, Request(1), Deadline);
        var sent = await capture.Requests.ReceiveAsync(Deadline);
        await wrong.PublishAsync(sent.With(FlowValue.From(99)));
        await observedReply.Task.WaitAsync(Deadline);
        pending.IsCompleted.ShouldBeFalse();
        var expected = sent.With(FlowValue.From(42));
        await capture.PublishAsync(expected);
        (await pending.WaitAsync(Deadline)).ShouldBeSameAs(expected);
        app.PendingRequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Capacity_cancellation_timeout_and_retry_release_the_single_registry()
    {
        var clock = new FakeTimeProvider();
        await using var app = new FlowApplication(maxPendingRequests: 1, timeProvider: clock);
        var capture = new Capture();
        var component = app.AddWorkflow("main").Add("request", capture);
        app.RepliesFrom(component.Output);
        await app.StartAsync();
        using var cancellation = new CancellationTokenSource();
        var original = Request(1);
        var first = app.RequestAsync(component.Input, component.Output, original, Deadline, cancellation.Token);
        var firstSent = await capture.Requests.ReceiveAsync(Deadline);
        var capacity = await Should.ThrowAsync<InvalidOperationException>(() =>
            app.RequestAsync(component.Input, component.Output, Request(2), Deadline));
        capacity.Message.ShouldContain("CapacityReached");
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => first.WaitAsync(Deadline));
        app.PendingRequestCount.ShouldBe(0);

        var expired = app.RequestAsync(component.Input, component.Output, original, TimeSpan.FromSeconds(1));
        var expiredSent = await capture.Requests.ReceiveAsync(Deadline);
        clock.Advance(TimeSpan.FromSeconds(1));
        await Should.ThrowAsync<TimeoutException>(() => expired.WaitAsync(Deadline));
        app.PendingRequestCount.ShouldBe(0);

        var retry = app.RequestAsync(component.Input, component.Output, original, Deadline);
        var retrySent = await capture.Requests.ReceiveAsync(Deadline);
        retrySent.CorrelationId.ShouldNotBe(firstSent.CorrelationId);
        retrySent.CorrelationId.ShouldNotBe(expiredSent.CorrelationId);
        await capture.PublishAsync(firstSent);
        await capture.PublishAsync(expiredSent);
        await capture.PublishAsync(retrySent);
        (await retry.WaitAsync(Deadline)).ShouldBeSameAs(retrySent);
        app.PendingRequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Deadline_includes_postponed_input_admission()
    {
        var clock = new FakeTimeProvider();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new GatedEcho(release.Task);
        await using var app = new FlowApplication(timeProvider: clock);
        var component = app.AddWorkflow("main").Add("gate", gate);
        app.RepliesFrom(component.Output);
        await app.StartAsync();
        try
        {
            (await app.SendAsync(component.Input, Request(0))).ShouldBeTrue();
            await gate.Entered.Task.WaitAsync(Deadline);
            var request = app.RequestAsync(component.Input, component.Output, Request(1), TimeSpan.FromSeconds(1));
            app.PendingRequestCount.ShouldBe(1);
            clock.Advance(TimeSpan.FromSeconds(1));
            await Should.ThrowAsync<TimeoutException>(() => request.WaitAsync(Deadline));
            app.PendingRequestCount.ShouldBe(0);
        }
        finally { release.TrySetResult(); }
        await app.StopAsync().WaitAsync(Deadline);
        gate.ProcessCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Shutdown_settles_pending_requests_and_rejects_new_ones()
    {
        await using var app = new FlowApplication();
        var capture = new Capture();
        var component = app.AddWorkflow("main").Add("request", capture);
        app.RepliesFrom(component.Output);
        await app.StartAsync();
        var pending = app.RequestAsync(component.Input, component.Output, Request(1), Deadline);
        await capture.Requests.ReceiveAsync(Deadline);
        await app.StopAsync().WaitAsync(Deadline);
        await Should.ThrowAsync<OperationCanceledException>(() => pending.WaitAsync(Deadline));
        app.PendingRequestCount.ShouldBe(0);
        Should.Throw<InvalidOperationException>(() => app.RequestAsync(component.Input, component.Output, Request(2), Deadline));
    }

    private static FlowMessage Request(int value, TraceId? trace = null)
        => FlowMessage.Create(FlowValue.From(value), traceId: trace);

    private sealed class Capture : FlowNode
    {
        public BufferBlock<FlowMessage> Requests { get; } = new();
        protected override Task ProcessAsync(FlowMessage message)
        {
            Requests.Post(message);
            return Task.CompletedTask;
        }
        public ValueTask PublishAsync(FlowMessage message) => EmitAsync(message);
        protected override ValueTask OnDisposeAsync() { Requests.Complete(); return ValueTask.CompletedTask; }
    }

    private sealed class Echo : FlowNode
    {
        protected override async Task ProcessAsync(FlowMessage message) => await EmitAsync(message);
    }

    private sealed class Discard : IFlowComponent
    {
        private readonly ActionBlock<FlowMessage> _input;
        public Discard(TaskCompletionSource observed)
        {
            _input = new(_ => observed.TrySetResult());
            Ports = new(inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = _input });
        }
        public FlowPorts Ports { get; }
        public Task Completion => _input.Completion;
        public void Complete() => _input.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)_input).Fault(error);
        public async ValueTask DisposeAsync() { Complete(); await Completion; }
    }

    private sealed class GatedEcho(Task release) : FlowNode(new FlowNodeOptions { InputCapacity = 1 })
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ProcessCalls { get; private set; }
        protected override async Task ProcessAsync(FlowMessage message)
        {
            ProcessCalls++;
            Entered.TrySetResult();
            await release.WaitAsync(Stopping);
            await EmitAsync(message);
        }
    }
}
