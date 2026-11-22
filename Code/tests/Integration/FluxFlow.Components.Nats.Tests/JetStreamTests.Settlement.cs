using System.Diagnostics;
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
    private static async Task SendSettlement(JetStreamSettlementNode node, BufferBlock<FlowMessage> output, object command, bool rejected = false)
    {
        var input = Value(command);
        (await node.Input.SendAsync(input).WaitAsync(Deadline)).ShouldBeTrue();
        var result = await output.ReceiveAsync(Deadline);
        result.IsError.ShouldBe(rejected, result.Error?.Message);
        if (rejected) result.CausationId.ShouldBe(input.MessageId);
        if (!rejected) result.Value.Json.GetRawText().ShouldBe(input.Value.Json.GetRawText());
    }

    [Fact]
    public async Task Delayed_nak_redelivers_and_terminate_emits_a_broker_advisory()
    {
        await using var session = new NatsSession(Options);
        var native = await session.OpenAsync(default);
        var js = new NatsJSContext(native);
        await using var terminations = await native.SubscribeCoreAsync<byte[]>("$JS.EVENT.ADVISORY.CONSUMER.MSG_TERMINATED.TRIAL.worker");
        await using var consumer = new JetStreamConsumer(session, Consumer with { AckWaitSeconds = 30 });
        var received = Capture(consumer.Output);
        await consumer.StartAsync().WaitAsync(Deadline);
        await using var settlement = new JetStreamSettlementNode(consumer);
        var results = Capture(settlement.Output);
        var publication = await js.PublishAsync("events.one", new byte[] { 0, 255 });
        publication.EnsureSuccess();
        var first = await Receive(received);
        await SendSettlement(settlement, results, new JetStreamSettlement(first.SettlementToken, "Ack", 2), rejected: true);
        var elapsed = Stopwatch.StartNew();
        // A workflow mapping can keep the delivery's SettlementToken and supply its other fields in camel case.
        await SendSettlement(settlement, results, new { first.SettlementToken, action = "Nak", delaySeconds = 2 });
        (await js.GetConsumerAsync("TRIAL", "worker")).Info.NumAckPending.ShouldBe(1);
        await SendSettlement(settlement, results, new JetStreamSettlement(first.SettlementToken), rejected: true);
        var second = await Receive(received);
        elapsed.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2));
        second.Metadata.Sequence.Stream.ShouldBe(publication.Seq);
        second.Metadata.NumDelivered.ShouldBe(2UL);
        second.SettlementToken.ShouldNotBe(first.SettlementToken);
        second.Message.DataBase64.ShouldBe("AP8=");
        await SendSettlement(settlement, results, new JetStreamSettlement(second.SettlementToken, "Terminate"));
        using var deadline = new CancellationTokenSource(Deadline);
        var advisory = await terminations.Msgs.ReadAsync(deadline.Token);
        using var body = JsonDocument.Parse(advisory.Data!);
        body.RootElement.GetProperty("stream_seq").GetUInt64().ShouldBe(publication.Seq);
        (await js.GetConsumerAsync("TRIAL", "worker")).Info.NumAckPending.ShouldBe(0);
        await SendSettlement(settlement, results, new JetStreamSettlement(second.SettlementToken), rejected: true);
    }

    [Fact]
    public async Task Settlement_after_broker_failure_reports_the_original_failure()
    {
        await using var session = new NatsSession(Options);
        var js = new NatsJSContext(await session.OpenAsync(default));
        await using var consumer = new JetStreamConsumer(session, Consumer with { MaxInFlight = 1, AckWaitSeconds = 30 });
        var received = Capture(consumer.Output);
        await consumer.StartAsync().WaitAsync(Deadline);
        await using var settlement = new JetStreamSettlementNode(consumer);
        var results = Capture(settlement.Output);
        (await js.PublishAsync("events.one", new byte[] { 1 })).EnsureSuccess();
        var first = await Receive(received);
        await _broker.StopAsync();
        var failure = await Should.ThrowAsync<NatsConnectionFailedException>(() => consumer.ProductionCompletion.WaitAsync(Deadline));
        await settlement.Input.SendAsync(Value(new JetStreamSettlement(first.SettlementToken))).WaitAsync(Deadline);
        var result = await results.ReceiveAsync(Deadline);
        result.Error.ShouldNotBeNull().Message.ShouldBe(failure.Message);
    }

    [Fact]
    public async Task Repeated_progress_extends_ack_wait_and_the_original_token_can_ack_during_stop()
    {
        await using var session = new NatsSession(Options);
        var js = new NatsJSContext(await session.OpenAsync(default));
        await using var consumer = new JetStreamConsumer(session, Consumer with { AckWaitSeconds = 2 });
        var received = Capture(consumer.Output);
        await consumer.StartAsync().WaitAsync(Deadline);
        await using var settlement = new JetStreamSettlementNode(consumer);
        var results = Capture(settlement.Output);
        (await js.PublishAsync("events.one", new byte[] { 1 })).EnsureSuccess();
        var first = await Receive(received);
        var elapsed = Stopwatch.StartNew();
        do
        {
            await Task.Delay(350);
            await SendSettlement(settlement, results, new JetStreamSettlement(first.SettlementToken, "Progress"));
        } while (elapsed.Elapsed < TimeSpan.FromSeconds(3));
        var state = (await js.GetConsumerAsync("TRIAL", "worker")).Info;
        state.Delivered.ConsumerSeq.ShouldBe(1UL);
        state.NumAckPending.ShouldBe(1);
        consumer.StopProduction();
        await consumer.ProductionCompletion.WaitAsync(Deadline);
        // Omitting Action is the existing shorthand for Ack.
        await SendSettlement(settlement, results, new { first.SettlementToken });
        (await js.GetConsumerAsync("TRIAL", "worker")).Info.NumAckPending.ShouldBe(0);
        await SendSettlement(settlement, results, new JetStreamSettlement(first.SettlementToken, "Progress"), rejected: true);
        received.Count.ShouldBe(0);
    }
}
