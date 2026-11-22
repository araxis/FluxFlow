using FluxFlow.Components.Nats.JetStream;
using NATS.Client.Core;
using NATS.Client.JetStream;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Nats.Tests;

public sealed partial class JetStreamTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Consumer_recovers_durable_deliveries_across_reconnect(bool pendingAck, bool stopDuringReconnect)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        var token = deadline.Token;
        await using var observer = new NatsConnection(Options);
        var js = new NatsJSContext(observer);
        await using var session = new NatsSession(Options);
        var options = Consumer with { MaxInFlight = 1, AckWaitSeconds = stopDuringReconnect ? 2 : 30 };
        await using var consumer = new JetStreamConsumer(session, options);
        var output = Capture(consumer.Output);
        await consumer.StartAsync(token);
        var durable = await js.GetConsumerAsync("TRIAL", "worker", token);
        if (pendingAck && !stopDuringReconnect)
        {
            // Give the broker room for two deliveries so it cannot mask a lost local slot limit.
            durable = await js.UpdateConsumerAsync("TRIAL", durable.Info.Config with { MaxAckPending = 2 }, token);
        }
        JetStreamDelivery? first = null;
        if (pendingAck)
        {
            var stored = await js.PublishAsync("events.one", new byte[] { 0, 255 }, cancellationToken: token);
            stored.EnsureSuccess();
            first = await Receive(output);
            first.Message.DataBase64.ShouldBe("AP8=");
            first.Metadata.Sequence.Stream.ShouldBe(stored.Seq);
            first.Metadata.NumDelivered.ShouldBe(1UL);
        }
        else
        {
            // Interrupt a pull that has actually reached the server.
            while (durable.Info.NumWaiting == 0)
            {
                await Task.Delay(20, token);
                await durable.RefreshAsync(token);
            }
        }

        var native = await session.OpenAsync(token);
        var reconnecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        native.OnConnectingAsync = async endpoint =>
        {
            reconnecting.TrySetResult();
            await release.Task.WaitAsync(token);
            return endpoint;
        };
        native.ConnectionOpened += (_, _) => { recovered.TrySetResult(); return ValueTask.CompletedTask; };
        await using var newSession = stopDuringReconnect ? new NatsSession(Options) : null;
        await using var rerun = newSession is null ? null : new JetStreamConsumer(newSession, options with
        {
            CreateConsumer = false, Stream = options.Stream with { CreateStream = false }
        });
        var current = rerun ?? consumer;
        var deliveries = rerun is null ? output : Capture(rerun.Output);
        ulong queuedSequence;
        try
        {
            await native.ReconnectAsync();
            await reconnecting.Task.WaitAsync(token);
            if (!pendingAck)
            {
                // Keep the socket down past the outstanding pull's one-second expiry.
                await Task.Delay(Options.RequestTimeout, token);
                await durable.RefreshAsync(token);
                durable.Info.NumWaiting.ShouldBe(0);
            }
            var queued = await js.PublishAsync("events.one", new byte[] { 254, 0 }, cancellationToken: token);
            queued.EnsureSuccess();
            queuedSequence = queued.Seq;
            if (pendingAck)
            {
                await durable.RefreshAsync(token);
                durable.Info.NumAckPending.ShouldBe(1);
                durable.Info.NumPending.ShouldBe(1UL);
            }
            if (rerun is not null)
            {
                consumer.Complete();
                await consumer.Completion.WaitAsync(TimeSpan.FromSeconds(5), token);
                await rerun.StartAsync(token);
            }
            output.Count.ShouldBe(0);
        }
        finally { release.TrySetResult(); }
        await recovered.Task.WaitAsync(token);
        await native.PingAsync(token);

        if (first is not null)
        {
            var toSettle = first;
            if (rerun is not null)
            {
                toSettle = await Receive(deliveries);
                toSettle.Metadata.Sequence.Stream.ShouldBe(first.Metadata.Sequence.Stream);
                toSettle.Metadata.NumDelivered.ShouldBe(2UL);
                toSettle.Message.DataBase64.ShouldBe(first.Message.DataBase64);
                toSettle.SettlementToken.ShouldNotBe(first.SettlementToken);
                await Should.ThrowAsync<InvalidOperationException>(() => rerun.SettleAsync(new(first.SettlementToken), token));
            }
            await durable.RefreshAsync(token);
            durable.Info.NumAckPending.ShouldBe(1);
            durable.Info.NumPending.ShouldBe(1UL);
            durable.Info.NumWaiting.ShouldBe(0);
            deliveries.Count.ShouldBe(0);
            await current.SettleAsync(new(toSettle.SettlementToken), token);
        }
        var delivery = await Receive(deliveries);
        delivery.Metadata.Sequence.Stream.ShouldBe(queuedSequence);
        delivery.Message.DataBase64.ShouldBe("/gA=");
        await current.SettleAsync(new(delivery.SettlementToken), token);
        await Should.ThrowAsync<InvalidOperationException>(() => current.SettleAsync(new(delivery.SettlementToken), token));
        current.Complete();
        await current.Completion.WaitAsync(token);
        await durable.RefreshAsync(token);
        durable.Info.NumAckPending.ShouldBe(0);
        durable.Info.NumPending.ShouldBe(0UL);
        durable.Info.AckFloor.StreamSeq.ShouldBe(queuedSequence);
        deliveries.Count.ShouldBe(0);
        output.Count.ShouldBe(0);
    }
}
