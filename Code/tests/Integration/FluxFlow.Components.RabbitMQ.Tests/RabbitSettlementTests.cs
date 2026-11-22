using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using RabbitMQ.Client;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.RabbitMQ.Tests;

public sealed partial class RabbitDeliveryTests
{
    [Fact]
    public async Task Acknowledge_ports_settle_only_the_selected_delivery_and_reject_old_tokens_after_Stop()
    {
        const string work = "settlement-work", rejected = "settlement-rejected";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        await using var connection = new RabbitConnection(Factory());
        await using var broker = await connection.OpenChannelAsync(true, token);
        await broker.QueueDeclareAsync(rejected, true, false, false, cancellationToken: token);
        await broker.QueueDeclareAsync(work, true, false, false, new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = "", ["x-dead-letter-routing-key"] = rejected
        }, cancellationToken: token);
        var options = new RabbitConsumerOptions { Queue = work, Prefetch = 1 };
        string oldToken;
        await using (var consumer = new RabbitConsumer(connection, options))
        await using (var acknowledge = new RabbitSettlement(consumer))
        await using (var deadLetters = new RabbitConsumer(connection, new() { Queue = rejected }))
        await using (var acknowledgeDeadLetter = new RabbitSettlement(deadLetters))
        {
            var deliveries = Capture(consumer.Output);
            var results = Capture(acknowledge.Output);
            var dead = Capture(deadLetters.Output);
            var deadResults = Capture(acknowledgeDeadLetter.Output);
            await consumer.StartAsync(token);
            await deadLetters.StartAsync(token);

            await Publish("default-ack");
            var first = await deliveries.ReceiveAsync(token);
            var defaultAck = Message(new { SettlementToken = Token(first), Extra = "keep" });
            await Settle(acknowledge, results, defaultAck);

            await Publish("reject");
            var next = await deliveries.ReceiveAsync(token);
            next.Value.Json.GetProperty("Properties").GetProperty("MessageId").GetString().ShouldBe("reject");
            await Settle(acknowledge, results, defaultAck, expectError: true);
            await Settle(acknowledge, results, Message(new { SettlementToken = Token(next), Action = "Reject" }));
            var rejectedDelivery = await dead.ReceiveAsync(token);
            rejectedDelivery.Value.Json.GetProperty("Properties").GetProperty("MessageId").GetString().ShouldBe("reject");

            await Publish("requeue");
            var pending = await deliveries.ReceiveAsync(token);
            var wrongConsumer = Message(new { SettlementToken = Token(rejectedDelivery), Action = "Ack" });
            await Settle(acknowledge, results, wrongConsumer, expectError: true);
            await Settle(acknowledgeDeadLetter, deadResults, wrongConsumer);
            var requeue = Message(new { SettlementToken = Token(pending), Action = "Requeue" });
            await Settle(acknowledge, results, requeue);
            var replay = await deliveries.ReceiveAsync(token);
            replay.Value.Json.GetProperty("Redelivered").GetBoolean().ShouldBeTrue();
            replay.Value.Json.GetProperty("Properties").GetProperty("MessageId").GetString().ShouldBe("requeue");
            oldToken = Token(replay);
            oldToken.ShouldNotBe(Token(pending));
            await Settle(acknowledge, results, requeue, expectError: true);

            acknowledge.Complete();
            await acknowledge.Completion.WaitAsync(token);
            (await acknowledge.Input.SendAsync(requeue, token)).ShouldBeFalse();
            consumer.StopProduction();
            await consumer.Completion.WaitAsync(token);
            deadLetters.StopProduction();
            await deadLetters.Completion.WaitAsync(token);
        }

        await using var rerun = new RabbitConsumer(connection, options);
        await using var fresh = new RabbitSettlement(rerun);
        var incoming = Capture(rerun.Output);
        var output = Capture(fresh.Output);
        await rerun.StartAsync(token);
        var redelivery = await incoming.ReceiveAsync(token);
        redelivery.Value.Json.GetProperty("Redelivered").GetBoolean().ShouldBeTrue();
        redelivery.Value.Json.GetProperty("Properties").GetProperty("MessageId").GetString().ShouldBe("requeue");
        Token(redelivery).ShouldNotBe(oldToken);
        await Settle(fresh, output, Message(new { SettlementToken = oldToken }), expectError: true);
        await Settle(fresh, output, Message(new { SettlementToken = Token(redelivery), Action = "Ack" }));
        fresh.Complete();
        await fresh.Completion.WaitAsync(token);
        rerun.StopProduction();
        await rerun.Completion.WaitAsync(token);
        (await broker.BasicGetAsync(work, true, token)).ShouldBeNull();
        (await broker.BasicGetAsync(rejected, true, token)).ShouldBeNull();

        async Task Publish(string id) => await broker.BasicPublishAsync("", work, true,
            new BasicProperties { MessageId = id }, new byte[] { 0, 255 }, token);

        async Task Settle(RabbitSettlement node, BufferBlock<FlowMessage> replies, FlowMessage command, bool expectError = false)
        {
            (await node.Input.SendAsync(command, token)).ShouldBeTrue();
            var reply = await replies.ReceiveAsync(token);
            if (expectError)
            {
                reply.Error.ShouldNotBeNull().Message.ShouldContain("already settled");
                reply.CausationId.ShouldBe(command.MessageId);
                reply.TraceId.ShouldBe(command.TraceId);
            }
            else reply.ShouldBeSameAs(command);
        }
    }
}
