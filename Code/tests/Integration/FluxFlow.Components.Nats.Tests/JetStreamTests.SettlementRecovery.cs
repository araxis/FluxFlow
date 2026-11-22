using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Nats.JetStream;
using FluxFlow.Core.Messaging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Nats.Tests;

public sealed partial class JetStreamTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lost_ACK_confirmation_recovers_with_same_token_or_rerun(bool stop)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        var token = deadline.Token;
        await using var observer = new NatsConnection(Options);
        var js = new NatsJSContext(observer);
        await using var session = new NatsSession(Options with { InboxPrefix = "_INBOX.settlement" });
        var native = await session.OpenAsync(token);
        var socketReady = new TaskCompletionSource<LossySocket>(TaskCreationOptions.RunContinuationsAsynchronously);
        native.OnSocketAvailableAsync = socket =>
        {
            var transport = new LossySocket(socket);
            socketReady.TrySetResult(transport);
            return ValueTask.FromResult<INatsSocketConnection>(transport);
        };
        await Reconnect();
        var socket = await socketReady.Task.WaitAsync(token);
        var options = Consumer with { MaxInFlight = 1, AckWaitSeconds = 30 };
        await using var consumer = new JetStreamConsumer(session, options);
        await using var settlement = new JetStreamSettlementNode(consumer);
        var deliveries = Capture(consumer.Output);
        var output = Capture(settlement.Output);
        await consumer.StartAsync(token);
        var firstStored = await js.PublishAsync("events.one", new byte[] { 1 }, cancellationToken: token);
        firstStored.EnsureSuccess();
        var first = await Receive(deliveries);
        first.Metadata.Sequence.Stream.ShouldBe(firstStored.Seq);
        var secondStored = await js.PublishAsync("events.one", new byte[] { 2 }, cancellationToken: token);
        secondStored.EnsureSuccess();
        await using var attempts = await observer.SubscribeCoreAsync<byte[]>("$JS.ACK.>", cancellationToken: token);
        await using var confirmations = await observer.SubscribeCoreAsync<byte[]>("_INBOX.settlement.>", cancellationToken: token);
        await observer.PingAsync(token);

        // Suppress only incoming bytes on this socket. The observer still sees both protocol messages.
        socket.DropIncoming = true;
        var original = await Submit(settlement, first.SettlementToken);
        var ack = await attempts.Msgs.ReadAsync(token);
        var confirmation = await confirmations.Msgs.ReadAsync(token);
        confirmation.Subject.ShouldBe(ack.ReplyTo);
        await socket.IncomingDropped.Task.WaitAsync(token);
        var durable = await js.GetConsumerAsync("TRIAL", "worker", token);
        durable.Info.AckFloor.StreamSeq.ShouldBe(firstStored.Seq);
        durable.Info.NumAckPending.ShouldBe(0);
        durable.Info.NumPending.ShouldBe(1UL);
        output.Count.ShouldBe(0);
        if (stop)
        {
            consumer.StopProduction();
            settlement.Complete();
        }
        var failed = await output.ReceiveAsync(TimeSpan.FromSeconds(5), token);
        failed.Error.ShouldNotBeNull().Code.ShouldBe("node.processing_failed");
        failed.CausationId.ShouldBe(original.MessageId);
        failed.CorrelationId.ShouldBe(original.CorrelationId);
        failed.TraceId.ShouldBe(original.TraceId);
        if (stop)
        {
            await settlement.Completion.WaitAsync(token);
            consumer.Complete();
            await consumer.Completion.WaitAsync(token);
            await session.DisposeAsync();
        }
        else await Reconnect();
        deliveries.Count.ShouldBe(0);
        attempts.Msgs.TryRead(out _).ShouldBeFalse();

        await using var nextSession = stop ? new NatsSession(Options) : null;
        await using var nextConsumer = nextSession is null ? null : new JetStreamConsumer(nextSession, options with
        {
            CreateConsumer = false, Stream = options.Stream with { CreateStream = false }
        });
        await using var nextSettlement = nextConsumer is null ? null : new JetStreamSettlementNode(nextConsumer);
        var current = nextSettlement ?? settlement;
        var currentOutput = nextSettlement is null ? output : Capture(nextSettlement.Output);
        var currentDeliveries = nextConsumer is null ? deliveries : Capture(nextConsumer.Output);
        if (nextConsumer is not null) await nextConsumer.StartAsync(token);
        var retry = await Submit(current, first.SettlementToken);
        var result = await currentOutput.ReceiveAsync(token);
        if (stop)
        {
            result.IsError.ShouldBeTrue();
            result.CausationId.ShouldBe(retry.MessageId);
            result.CorrelationId.ShouldBe(retry.CorrelationId);
        }
        else
        {
            result.Error.ShouldBeNull();
            result.ShouldBeSameAs(retry);
            var retriedAck = await attempts.Msgs.ReadAsync(token);
            retriedAck.Subject.ShouldBe(ack.Subject);
            retriedAck.ReplyTo.ShouldNotBe(ack.ReplyTo);
        }
        var second = await Receive(currentDeliveries);
        second.Metadata.Sequence.Stream.ShouldBe(secondStored.Seq);
        second.Metadata.NumDelivered.ShouldBe(1UL);
        var final = await Submit(current, second.SettlementToken);
        (await currentOutput.ReceiveAsync(token)).ShouldBeSameAs(final);
        (await attempts.Msgs.ReadAsync(token)).Subject.ShouldNotBe(ack.Subject);
        await durable.RefreshAsync(token);
        durable.Info.AckFloor.StreamSeq.ShouldBe(secondStored.Seq);
        durable.Info.NumAckPending.ShouldBe(0);
        attempts.Msgs.TryRead(out _).ShouldBeFalse();
        currentDeliveries.Count.ShouldBe(0);
        currentOutput.Count.ShouldBe(0);
        output.Count.ShouldBe(0);

        async Task<FlowMessage> Submit(JetStreamSettlementNode node, string handle)
        {
            var command = new JetStreamSettlement(handle);
            var input = FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(command)), CorrelationId.New());
            (await node.Input.SendAsync(input, token)).ShouldBeTrue();
            return input;
        }

        async Task Reconnect()
        {
            var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ValueTask Opened(object? sender, NatsEventArgs args) { connected.TrySetResult(); return ValueTask.CompletedTask; }
            native.ConnectionOpened += Opened;
            try
            {
                await native.ReconnectAsync();
                await connected.Task.WaitAsync(token);
                await native.PingAsync(token);
            }
            finally { native.ConnectionOpened -= Opened; }
        }
    }

    private sealed class LossySocket(INatsSocketConnection inner) : INatsSocketConnection
    {
        public volatile bool DropIncoming;
        public TaskCompletionSource IncomingDropped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer) => inner.SendAsync(buffer);
        public async ValueTask<int> ReceiveAsync(Memory<byte> buffer)
        {
            while (true)
            {
                var count = await inner.ReceiveAsync(buffer);
                if (count == 0 || !DropIncoming) return count;
                IncomingDropped.TrySetResult();
            }
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
