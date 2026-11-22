using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using NATS.Client.Core;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Nats.Tests;

public sealed partial class NatsDeliveryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Disconnect_during_publish_preserves_output_and_does_not_replay(bool completeFrame, bool stop)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        var token = deadline.Token;
        await using var observer = new NatsConnection(Options());
        await using var deliveries = await observer.SubscribeCoreAsync<byte[]>("trial.*", cancellationToken: token);
        await observer.PingAsync(token);

        await using var session = new NatsSession(Options() with { PublishTimeoutOnDisconnected = true });
        var native = await session.OpenAsync(token);
        var ready = new TaskCompletionSource<PublishCutSocket>(TaskCreationOptions.RunContinuationsAsynchronously);
        native.OnSocketAvailableAsync = inner =>
        {
            var socket = new PublishCutSocket(inner, completeFrame, token);
            return ValueTask.FromResult<INatsSocketConnection>(ready.TrySetResult(socket) ? socket : inner);
        };
        await native.ReconnectAsync();
        var transport = await ready.Task.WaitAsync(token);
        await native.PingAsync(token);

        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reconnect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        native.OnConnectingAsync = async endpoint =>
        {
            disconnected.TrySetResult();
            await reconnect.Task.WaitAsync(token);
            return endpoint;
        };
        var options = new NatsComponentOptions { TimeoutSeconds = 1 };
        await using var publisher = new NatsPublisher(session, options);
        var results = Capture(publisher.Output);
        try
        {
            var interrupted = await Send(publisher, "trial.cut", 1);
            await transport.Written.Task.WaitAsync(token);
            // Local enqueue completes even while the socket write is still in progress.
            (await results.ReceiveAsync(token)).ShouldBeSameAs(interrupted);
            if (completeFrame) await ExpectDelivery("trial.cut", 1);
            await transport.DisposeAsync();
            transport.Release.TrySetResult();
            await disconnected.Task.WaitAsync(token);
            var offline = await Send(publisher, "trial.offline", 2);
            if (stop) publisher.Complete();
            var failure = await results.ReceiveAsync(token);
            failure.Error.ShouldNotBeNull().Code.ShouldBe("node.processing_failed");
            failure.CausationId.ShouldBe(offline.MessageId);
            failure.TraceId.ShouldBe(offline.TraceId);
            failure.CorrelationId.ShouldBe(offline.CorrelationId);
            if (stop)
            {
                await publisher.Completion.WaitAsync(token);
                (await publisher.Input.SendAsync(offline, token)).ShouldBeFalse();
            }
        }
        finally
        {
            transport.Release.TrySetResult();
            reconnect.TrySetResult();
        }
        await native.PingAsync(token);
        if (stop) await session.DisposeAsync();

        await using var nextSession = stop ? new NatsSession(Options()) : null;
        await using var next = nextSession is null ? null : new NatsPublisher(nextSession, options);
        var current = next ?? publisher;
        var currentOutput = next is null ? results : Capture(next.Output);
        var fresh = await Send(current, "trial.fresh", 3);
        (await currentOutput.ReceiveAsync(token)).ShouldBeSameAs(fresh);
        await ExpectDelivery("trial.fresh", 3);
        current.Complete();
        await current.Completion.WaitAsync(token);
        var currentConnection = await (nextSession ?? session).OpenAsync(token);
        await currentConnection.PingAsync(token);
        await observer.PublishAsync("trial.end", new byte[] { 4 }, cancellationToken: token);
        await ExpectDelivery("trial.end", 4);
        results.Count.ShouldBe(0);
        currentOutput.Count.ShouldBe(0);

        async Task<FlowMessage> Send(NatsPublisher node, string subject, byte id)
        {
            var data = new NatsMessage { Subject = subject, DataBase64 = Convert.ToBase64String([id]) };
            var input = FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(data)), CorrelationId.New());
            (await node.Input.SendAsync(input, token)).ShouldBeTrue();
            return input;
        }

        async Task ExpectDelivery(string subject, byte id)
        {
            var received = await deliveries.Msgs.ReadAsync(token);
            received.Subject.ShouldBe(subject);
            received.Data.ShouldBe(new byte[] { id });
        }
    }

    private sealed class PublishCutSocket(INatsSocketConnection inner, bool completeFrame, CancellationToken token) : INatsSocketConnection
    {
        public TaskCompletionSource Written { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer)
        {
            var start = buffer.Span.IndexOf("PUB trial.cut "u8);
            if (start < 0) return await inner.SendAsync(buffer);
            var count = await inner.SendAsync(completeFrame ? buffer : buffer[..(start + 3)]);
            Written.TrySetResult();
            await Release.Task.WaitAsync(token);
            return count;
        }

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer) => inner.ReceiveAsync(buffer);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
