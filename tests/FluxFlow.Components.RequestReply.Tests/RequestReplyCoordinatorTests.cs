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
        request.Payload.ShouldBe("ping");
        request.CorrelationId.IsEmpty.ShouldBeFalse();
        bridge.InFlightCount.ShouldBe(1);
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
    public async Task EndToEnd_GraphHandler_RepliesThroughBridge()
    {
        await using var bridge = new RequestReplyCoordinator<string, string>();
        // The "graph": echo handler that preserves the correlation id via With().
        var handler = new ActionBlock<FlowMessage<string>>(
            request => bridge.Responses.Post(request.With($"echo:{request.Payload}")));
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
        var errors = Sink(bridge.Errors);

        await bridge.Responses.SendAsync(FlowMessage.Create("orphan", new CorrelationId("unknown")));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(RequestReplyErrorCodes.Unmatched);
        error.CorrelationId.ShouldBe(new CorrelationId("unknown"));
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }

    private sealed class FakeContext(string request, CorrelationId? correlationId = null)
        : IRequestContext<string, string>
    {
        private readonly TaskCompletionSource _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Request { get; } = request;
        public CorrelationId? CorrelationId { get; } = correlationId;
        public string? Replied { get; private set; }
        public Exception? Failed { get; private set; }
        public Task Settled => _settled.Task;

        public Task ReplyAsync(string response, CancellationToken cancellationToken = default)
        {
            Replied = response;
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
