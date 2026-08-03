using FluxFlow.Components.RequestReply;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.RequestReply.Tests;

public sealed class RequestReplyCoordinatorTests
{
    [Fact]
    public async Task Request_FlowsToOutput_WithMintedCorrelationId()
    {
        await using var bridge = new RequestReplyCoordinator<string, string>();
        var context = new FakeContext("ping");

        await bridge.Incoming.SendAsync(context);

        var request = await bridge.Output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        request.Value.ShouldBe("ping");
        request.CorrelationId.HasValue.ShouldBeTrue();
        request.CorrelationId.Value.IsEmpty.ShouldBeFalse();
        bridge.InFlightCount.ShouldBe(1);
    }

    [Fact]
    public async Task Request_output_fans_out_each_committed_request_to_all_active_subscribers()
    {
        var timeout = TimeSpan.FromSeconds(30);
        await using var bridge = new RequestReplyCoordinator<string, string>(
            new RequestReplyOptions
            {
                Mode = RequestReplyMode.FireAndForget,
                Capacity = 1
            });
        var first = Sink(bridge.Output);
        var second = Sink(bridge.Output);
        var expectedCorrelation = new CorrelationId("request-one");
        var firstContext = new FakeContext("one", expectedCorrelation);
        var secondContext = new FakeContext("two");

        (await bridge.Incoming.SendAsync(firstContext).WaitAsync(timeout)).ShouldBeTrue();
        (await bridge.Incoming.SendAsync(secondContext).WaitAsync(timeout)).ShouldBeTrue();

        var firstMessages = await ReceiveAsync(first, 2, timeout);
        var secondMessages = await ReceiveAsync(second, 2, timeout);
        await firstContext.Settled.WaitAsync(timeout);
        await secondContext.Settled.WaitAsync(timeout);

        firstMessages.Select(static message => message.Value).ShouldBe(["one", "two"]);
        secondMessages.Select(static message => message.Value).ShouldBe(["one", "two"]);
        firstMessages.Select(static message => message.MessageId)
            .ShouldBe(secondMessages.Select(static message => message.MessageId));
        firstMessages[0].CorrelationId.ShouldBe(expectedCorrelation);
        secondMessages[0].CorrelationId.ShouldBe(expectedCorrelation);
        firstMessages[1].CorrelationId.ShouldNotBeNull();
        firstMessages[1].CorrelationId!.Value.IsEmpty.ShouldBeFalse();
        firstMessages[1].CorrelationId.ShouldBe(secondMessages[1].CorrelationId);
        firstContext.Acknowledged.ShouldBeTrue();
        secondContext.Acknowledged.ShouldBeTrue();
        bridge.InFlightCount.ShouldBe(0);

        bridge.Complete();
        await bridge.Completion.WaitAsync(timeout);
        await Task.WhenAll(first.Completion, second.Completion).WaitAsync(timeout);
    }

    [Fact]
    public async Task HostSuppliedCorrelationId_IsHonoured()
    {
        await using var bridge = new RequestReplyCoordinator<string, string>();
        var id = new CorrelationId("trace-7");
        await bridge.Incoming.SendAsync(new FakeContext("ping", id));

        (await bridge.Output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).CorrelationId.ShouldBe(id);
    }

    [Fact]
    public async Task Response_RepliesToContext_AndEvicts()
    {
        await using var bridge = new RequestReplyCoordinator<string, string>();
        var context = new FakeContext("hello");
        await bridge.Incoming.SendAsync(context);

        var request = await bridge.Output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await bridge.Responses.SendAsync(request.With("world"));

        await context.Settled.WaitAsync(TimeSpan.FromSeconds(30));
        context.Replied.ShouldBe("world");
        context.Failed.ShouldBeNull();
        bridge.InFlightCount.ShouldBe(0);
    }

    [Fact]
    public async Task RequestReplyMode_EmitsPublishedEventAfterRequestReachesOutput()
    {
        await using var bridge = new RequestReplyCoordinator<string, string>();
        var events = Sink(bridge.Events);
        var context = new FakeContext("hello");

        await bridge.Incoming.SendAsync(context);
        var request = await bridge.Output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await bridge.Responses.SendAsync(request.With("world"));
        await context.Settled.WaitAsync(TimeSpan.FromSeconds(30));

        var received = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var published = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var replied = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));

        received.Name.ShouldBe(RequestReplyEvents.Received);
        received.CorrelationId.ShouldBe(request.CorrelationId);
        published.Name.ShouldBe(RequestReplyEvents.Published);
        published.CorrelationId.ShouldBe(request.CorrelationId);
        replied.Name.ShouldBe(RequestReplyEvents.Replied);
        replied.CorrelationId.ShouldBe(request.CorrelationId);
    }

    [Fact]
    public async Task EndToEnd_GraphHandler_RepliesThroughBridge()
    {
        await using var bridge = new RequestReplyCoordinator<string, string>();
        // The "graph": echo handler that preserves the correlation id via With().
        var handler = new ActionBlock<FlowMessage<string>>(
            request => bridge.Responses.Post(request.With($"echo:{request.Value}")));
        bridge.Output.LinkTo(handler);

        var context = new FakeContext("hi");
        await bridge.Incoming.SendAsync(context);

        await context.Settled.WaitAsync(TimeSpan.FromSeconds(30));
        context.Replied.ShouldBe("echo:hi");
    }

    [Fact]
    public async Task Timeout_FailsContext_AndEvicts()
    {
        var clock = new FakeTimeProvider();
        await using var bridge = new RequestReplyCoordinator<string, string>(
            new RequestReplyOptions
            {
                Timeout = TimeSpan.FromMilliseconds(200),
                SweepInterval = TimeSpan.FromMilliseconds(100)
            },
            clock);
        var context = new FakeContext("slow");
        await bridge.Incoming.SendAsync(context);
        await bridge.Output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)); // now in-flight

        clock.Advance(TimeSpan.FromMilliseconds(350)); // past deadline; fires a sweep

        await context.Settled.WaitAsync(TimeSpan.FromSeconds(30));
        context.Failed.ShouldBeOfType<TimeoutException>();
        context.Replied.ShouldBeNull();
        bridge.InFlightCount.ShouldBe(0);
    }

    [Fact]
    public async Task UnmatchedResponse_ReportsError()
    {
        await using var bridge = new RequestReplyCoordinator<string, string>();
        var events = Sink(bridge.Events);

        await bridge.Responses.SendAsync(FlowMessage.Create("orphan", new CorrelationId("unknown")));

        var error = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Attributes["code"].ShouldBe(RequestReplyErrorCodes.Unmatched);
        error.CorrelationId.ShouldBe(new CorrelationId("unknown"));
    }

    [Fact]
    public async Task NullRequestContext_ReportsError_AndContinues()
    {
        await using var bridge = new RequestReplyCoordinator<string, string>();
        var events = Sink(bridge.Events);

        await bridge.Incoming.SendAsync(null!);

        var error = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var flowEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Attributes["code"].ShouldBe(RequestReplyErrorCodes.InvalidRequestContext);
        error.CorrelationId.ShouldBeNull();
        flowEvent.Name.ShouldBe(RequestReplyEvents.Invalid);
        flowEvent.Level.ShouldBe(FlowEventLevel.Error);

        var context = new FakeContext("valid");
        await bridge.Incoming.SendAsync(context);

        var request = await bridge.Output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        request.Value.ShouldBe("valid");
        await bridge.Responses.SendAsync(request.With("ok"));
        await context.Settled.WaitAsync(TimeSpan.FromSeconds(30));
        context.Replied.ShouldBe("ok");
    }

    [Fact]
    public async Task NullResponseMessage_ReportsError_AndContinues()
    {
        await using var bridge = new RequestReplyCoordinator<string, string>();
        var events = Sink(bridge.Events);

        await bridge.Responses.SendAsync(null!);

        var error = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var flowEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Attributes["code"].ShouldBe(RequestReplyErrorCodes.InvalidResponseMessage);
        error.CorrelationId.ShouldBeNull();
        flowEvent.Name.ShouldBe(RequestReplyEvents.Invalid);
        flowEvent.Level.ShouldBe(FlowEventLevel.Error);

        var context = new FakeContext("valid");
        await bridge.Incoming.SendAsync(context);
        var request = await bridge.Output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await bridge.Responses.SendAsync(request.With("ok"));

        await context.Settled.WaitAsync(TimeSpan.FromSeconds(30));
        context.Replied.ShouldBe("ok");
    }

    [Fact]
    public async Task Fault_FailsInFlightCallers_AndFaultsCompletion()
    {
        var timeout = TimeSpan.FromSeconds(30);
        await using var bridge = new RequestReplyCoordinator<string, string>();
        var context = new FakeContext("pending");
        var receive = bridge.Output.ReceiveAsync();

        (await bridge.Incoming.SendAsync(context).WaitAsync(timeout)).ShouldBeTrue();
        var request = await receive.WaitAsync(timeout);
        request.Value.ShouldBe("pending");
        bridge.InFlightCount.ShouldBe(1);

        bridge.Fault(new InvalidOperationException("boom"));

        // The in-flight caller is failed (not left hanging) with the fault.
        await context.Settled.WaitAsync(timeout);
        context.Failed.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("boom");
        context.Replied.ShouldBeNull();
        bridge.InFlightCount.ShouldBe(0);

        // Completion surfaces the fault.
        var completionFailure = await Should.ThrowAsync<InvalidOperationException>(
            () => bridge.Completion.WaitAsync(timeout));
        completionFailure.Message.ShouldBe("boom");
    }

    [Fact]
    public async Task Complete_FailsInFlightCallers_AndSettlesCompletion()
    {
        await using var bridge = new RequestReplyCoordinator<string, string>();
        var context = new FakeContext("pending");
        await bridge.Incoming.SendAsync(context);
        await bridge.Output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));

        bridge.Complete();

        await context.Settled.WaitAsync(TimeSpan.FromSeconds(30));
        context.Failed.ShouldBeOfType<OperationCanceledException>();
        context.Replied.ShouldBeNull();
        await bridge.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task DisposeAsync_FailsInFlightCallers_AndSettlesCompletion()
    {
        var bridge = new RequestReplyCoordinator<string, string>();
        var context = new FakeContext("pending");
        await bridge.Incoming.SendAsync(context);
        await bridge.Output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));

        await bridge.DisposeAsync();

        await context.Settled.WaitAsync(TimeSpan.FromSeconds(30));
        context.Failed.ShouldBeOfType<OperationCanceledException>();
        context.Replied.ShouldBeNull();
        await bridge.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Coordinator_IsAFlowNode()
    {
        await using var bridge = new RequestReplyCoordinator<string, string>();
        bridge.ShouldBeAssignableTo<IFlowNode>();
    }

    [Fact]
    public void Constructor_rejects_invalid_options()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new RequestReplyCoordinator<string, string>(
                new RequestReplyOptions { Mode = (RequestReplyMode)999 }))
            .Message.ShouldContain("mode", Case.Insensitive);

        Should.Throw<ArgumentOutOfRangeException>(() =>
            new RequestReplyCoordinator<string, string>(
                new RequestReplyOptions { Capacity = 0 }))
            .Message.ShouldContain("Capacity");

        Should.Throw<ArgumentOutOfRangeException>(() =>
            new RequestReplyCoordinator<string, string>(
                new RequestReplyOptions { Timeout = TimeSpan.Zero }))
            .Message.ShouldContain("Timeout");

        Should.Throw<ArgumentOutOfRangeException>(() =>
            new RequestReplyCoordinator<string, string>(
                new RequestReplyOptions
                {
                    Mode = RequestReplyMode.FireAndForget,
                    SweepInterval = TimeSpan.Zero
                }))
            .Message.ShouldContain("Sweep interval");
    }

    [Fact]
    public void RequestReplyOptions_reject_invalid_values_when_assigned()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new RequestReplyOptions
        {
            Mode = (RequestReplyMode)999
        }).Message.ShouldContain("mode", Case.Insensitive);
        Should.Throw<ArgumentOutOfRangeException>(() => new RequestReplyOptions
        {
            Capacity = 0
        }).Message.ShouldContain("Capacity");
        Should.Throw<ArgumentOutOfRangeException>(() => new RequestReplyOptions
        {
            Timeout = TimeSpan.Zero
        }).Message.ShouldContain("Timeout");
        Should.Throw<ArgumentOutOfRangeException>(() => new RequestReplyOptions
        {
            SweepInterval = TimeSpan.Zero
        }).Message.ShouldContain("Sweep interval");
    }

    [Fact]
    public async Task FireAndForget_PublishesThenAcknowledges_WithoutHoldingInFlight()
    {
        await using var bridge = new RequestReplyCoordinator<string, string>(
            new RequestReplyOptions { Mode = RequestReplyMode.FireAndForget });
        var context = new FakeContext("ingest");

        await bridge.Incoming.SendAsync(context);

        // The request is published into the graph...
        var request = await bridge.Output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        request.Value.ShouldBe("ingest");
        request.CorrelationId.HasValue.ShouldBeTrue();
        request.CorrelationId.Value.IsEmpty.ShouldBeFalse();

        // ...and the caller is acknowledged immediately, never held in flight or replied to.
        await context.Settled.WaitAsync(TimeSpan.FromSeconds(30));
        context.Acknowledged.ShouldBeTrue();
        context.Replied.ShouldBeNull();
        context.Failed.ShouldBeNull();
        bridge.InFlightCount.ShouldBe(0);
    }

    [Fact]
    public async Task FireAndForget_IgnoresLateResponses()
    {
        await using var bridge = new RequestReplyCoordinator<string, string>(
            new RequestReplyOptions { Mode = RequestReplyMode.FireAndForget });
        var events = Sink(bridge.Events);
        var context = new FakeContext("ingest");
        await bridge.Incoming.SendAsync(context);
        var request = await bridge.Output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));

        // A response for a fire-and-forget request has nothing to match; it is reported
        // unmatched like any orphan rather than replying to the (already-acked) caller.
        await bridge.Responses.SendAsync(request.With("late"));

        var error = await ReceiveErrorEventAsync(events);
        error.Attributes["code"].ShouldBe(RequestReplyErrorCodes.Unmatched);
        context.Replied.ShouldBeNull();
    }

    private static async Task<FlowEvent> ReceiveErrorEventAsync(BufferBlock<FlowEvent> events)
    {
        while (true)
        {
            var @event = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
            if (@event.Attributes.ContainsKey("code"))
            {
                return @event;
            }
        }
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }

    private static async Task<IReadOnlyList<T>> ReceiveAsync<T>(
        BufferBlock<T> sink,
        int count,
        TimeSpan timeout)
    {
        var items = new List<T>(count);
        for (var index = 0; index < count; index++)
        {
            items.Add(await sink.ReceiveAsync().WaitAsync(timeout));
        }

        return items;
    }

    private sealed class FakeContext(string request, CorrelationId? correlationId = null)
        : IRequestContext<string, string>
    {
        private readonly TaskCompletionSource _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Request { get; } = request;
        public CorrelationId? CorrelationId { get; } = correlationId;
        public string? Replied { get; private set; }
        public bool Acknowledged { get; private set; }
        public Exception? Failed { get; private set; }
        public Task Settled => _settled.Task;

        public Task ReplyAsync(string response, CancellationToken cancellationToken = default)
        {
            Replied = response;
            _settled.TrySetResult();
            return Task.CompletedTask;
        }

        public Task AcknowledgeAsync(CancellationToken cancellationToken = default)
        {
            Acknowledged = true;
            _settled.TrySetResult();
            return Task.CompletedTask;
        }

        public Task FailAsync(Exception error, CancellationToken cancellationToken = default)
        {
            Failed = error;
            _settled.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
