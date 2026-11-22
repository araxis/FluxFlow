using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.RabbitMQ.Tests;

public sealed partial class RabbitDeliveryTests
{
    [Fact]
    public async Task Feedback_reads_only_the_delivery_token_and_ignores_unrelated_action_values()
    {
        await using var connection = new RabbitConnection(Factory());
        await using var consumer = new RabbitConsumer(connection, new()
        { Queue = "feedback-fields", Prefetch = 1, Topology = new() { Queues = [new("feedback-fields")] } });
        var output = Capture(consumer.Output);
        var events = Capture(consumer.Events);
        await consumer.StartAsync().WaitAsync(Deadline);
        await using var publisher = new RabbitPublisher(connection);
        var confirms = Capture(publisher.Output);
        foreach (var port in new[] { consumer.ACK, consumer.NACK })
        {
            await publisher.Input.SendAsync(Message(new AmqpPublish { RoutingKey = "feedback-fields", Body = "AP8=" }));
            (await confirms.ReceiveAsync(Deadline)).Error.ShouldBeNull();
            var delivery = await output.ReceiveAsync(Deadline);
            var feedback = Message(new { settlementToken = Token(delivery), Action = new { Code = 42 }, Body = "keep" });
            var original = feedback.Value.Json.GetRawText();
            await port.SendAsync(feedback);
            await port.SendAsync(feedback);
            // Only the duplicate should be rejected, not the first command's unrelated fields.
            var rejection = await Rejected(events);
            rejection.Message.ShouldNotBeNull().ShouldContain("already settled");
            feedback.Value.Json.GetRawText().ShouldBe(original);
            consumer.Completion.IsCompleted.ShouldBeFalse();
        }
        consumer.StopProduction(); await consumer.Completion.WaitAsync(Deadline);
        publisher.Input.Complete(); await publisher.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Feedback_correlates_out_of_order_and_rejects_duplicates_wrong_consumers_and_previous_runs()
    {
        await using var connection = new RabbitConnection(Factory());
        await using var consumer = new RabbitConsumer(connection, new()
        {
            Queue = "feedback", Prefetch = 2, Capacity = 2, Topology = new() { Queues = [new("feedback")] }
        });
        consumer.TerminalInputs.ShouldContain(consumer.ACK);
        consumer.TerminalInputs.ShouldContain(consumer.NACK);
        var output = Capture(consumer.Output);
        var events = Capture(consumer.Events);
        await consumer.StartAsync().WaitAsync(Deadline);
        await using var publisher = new RabbitPublisher(connection);
        var confirms = Capture(publisher.Output);
        for (var index = 1; index <= 3; index++)
        {
            await publisher.Input.SendAsync(Message(new AmqpPublish
            {
                RoutingKey = "feedback", Body = Convert.ToBase64String(new[] { (byte)index }),
                Properties = new() { CorrelationId = "same-business-id" }
            }));
            (await confirms.ReceiveAsync(Deadline)).Error.ShouldBeNull();
        }
        var first = await output.ReceiveAsync(Deadline);
        var second = await output.ReceiveAsync(Deadline);
        first.Value.Json.GetProperty("Body").GetString().ShouldBe("AQ==");
        second.Value.Json.GetProperty("Body").GetString().ShouldBe("Ag==");
        await NoDelivery(output);

        await using var other = new RabbitConsumer(connection, new()
        { Queue = "other", Topology = new() { Queues = [new("other")] } });
        var otherEvents = Capture(other.Events);
        await other.StartAsync().WaitAsync(Deadline);
        await other.ACK.SendAsync(first);
        await Rejected(otherEvents);
        await NoDelivery(output);

        // An Action field must not override the port. ACK the second, leaving the first pending.
        var secondCommand = Message(new AmqpSettlement
        { SettlementToken = Token(second), Action = "Requeue" });
        await consumer.ACK.SendAsync(secondCommand);
        var third = await output.ReceiveAsync(Deadline);
        third.Value.Json.GetProperty("Body").GetString().ShouldBe("Aw==");
        await consumer.ACK.SendAsync(secondCommand);
        await Rejected(events);
        foreach (var invalid in new[] { Message(new { }), Message(new { SettlementToken = "unknown" }),
            Message(new { SettlementToken = 42 }), Message(new { SettlementToken = " " }), Message("invalid") })
        {
            await consumer.NACK.SendAsync(invalid);
            await Rejected(events);
        }
        await consumer.ACK.SendAsync(third);
        await consumer.ACK.SendAsync(third);
        await Rejected(events); // confirms the preceding settlement was processed
        consumer.Completion.IsCompleted.ShouldBeFalse();
        consumer.StopProduction();
        await consumer.Completion.WaitAsync(Deadline);
        (await consumer.ACK.SendAsync(first)).ShouldBeFalse();
        (await consumer.NACK.SendAsync(first)).ShouldBeFalse();
        await using var rerun = new RabbitConsumer(connection, new() { Queue = "feedback", Prefetch = 1 });
        var replay = Capture(rerun.Output);
        var rerunEvents = Capture(rerun.Events);
        await rerun.StartAsync().WaitAsync(Deadline);
        var redelivered = await replay.ReceiveAsync(Deadline);
        redelivered.Value.Json.GetProperty("Body").GetString().ShouldBe("AQ==");
        redelivered.Value.Json.GetProperty("Redelivered").GetBoolean().ShouldBeTrue();
        Token(redelivered).ShouldNotBe(Token(first));
        await rerun.ACK.SendAsync(first);
        await Rejected(rerunEvents);
        await rerun.ACK.SendAsync(redelivered);
        await rerun.ACK.SendAsync(redelivered);
        await Rejected(rerunEvents);
        await NoDelivery(replay);
        rerun.StopProduction(); await rerun.Completion.WaitAsync(Deadline);
        other.StopProduction(); await other.Completion.WaitAsync(Deadline);
        publisher.Input.Complete(); await publisher.Completion.WaitAsync(Deadline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nack_uses_configured_requeue_policy_and_never_acknowledges_from_the_payload_action(bool requeue)
    {
        await using var connection = new RabbitConnection(Factory());
        await using var consumer = new RabbitConsumer(connection, new()
        {
            Queue = "nack", Prefetch = 1, Capacity = 1, RequeueOnNack = requeue,
            Topology = new() { Queues = [new("nack")] }
        });
        var output = Capture(consumer.Output);
        var events = Capture(consumer.Events);
        await consumer.StartAsync().WaitAsync(Deadline);
        await using var publisher = new RabbitPublisher(connection);
        var confirms = Capture(publisher.Output);
        foreach (var body in new[] { "AP8=", "Ag==" })
        {
            await publisher.Input.SendAsync(Message(new AmqpPublish { RoutingKey = "nack", Body = body }));
            (await confirms.ReceiveAsync(Deadline)).Error.ShouldBeNull();
        }
        var first = await output.ReceiveAsync(Deadline);
        await consumer.NACK.SendAsync(Message(new AmqpSettlement { SettlementToken = Token(first), Action = "Ack" }));
        var next = await output.ReceiveAsync(Deadline);
        if (requeue)
        {
            next.Value.Json.GetProperty("Body").GetString().ShouldBe("AP8=");
            next.Value.Json.GetProperty("Redelivered").GetBoolean().ShouldBeTrue();
            await consumer.ACK.SendAsync(next);
            next = await output.ReceiveAsync(Deadline);
        }
        next.Value.Json.GetProperty("Body").GetString().ShouldBe("Ag==");
        await consumer.ACK.SendAsync(next);
        await consumer.ACK.SendAsync(next);
        await Rejected(events);
        await NoDelivery(output);
        consumer.StopProduction(); await consumer.Completion.WaitAsync(Deadline);
        publisher.Input.Complete(); await publisher.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Stop_during_sustained_feedback_closes_inputs_and_leaves_pending_work_for_redelivery()
    {
        await using var connection = new RabbitConnection(Factory());
        await using var consumer = new RabbitConsumer(connection, new()
        {
            Queue = "busy-feedback", Prefetch = 1, Capacity = 1, RequeueOnNack = true,
            Topology = new() { Queues = [new("busy-feedback")] }
        });
        var active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var feedback = new ActionBlock<FlowMessage>(async message =>
        {
            if (Interlocked.Increment(ref count) >= 100) active.TrySetResult();
            await consumer.NACK.SendAsync(message);
        }, new() { BoundedCapacity = 1 });
        consumer.Output.LinkTo(feedback, new() { PropagateCompletion = true });
        await consumer.StartAsync().WaitAsync(Deadline);
        await using var publisher = new RabbitPublisher(connection);
        var confirms = Capture(publisher.Output);
        await publisher.Input.SendAsync(Message(new AmqpPublish { RoutingKey = "busy-feedback", Body = "AP8=" }));
        (await confirms.ReceiveAsync(Deadline)).Error.ShouldBeNull();
        await active.Task.WaitAsync(Deadline);
        consumer.StopProduction();
        await Task.WhenAll(consumer.Completion, feedback.Completion).WaitAsync(Deadline);
        (await consumer.NACK.SendAsync(Message(new { SettlementToken = "late" }))).ShouldBeFalse();
        await using var rerun = new RabbitConsumer(connection, new() { Queue = "busy-feedback" });
        var output = Capture(rerun.Output); var events = Capture(rerun.Events);
        await rerun.StartAsync().WaitAsync(Deadline);
        var delivery = await output.ReceiveAsync(Deadline);
        delivery.Value.Json.GetProperty("Body").GetString().ShouldBe("AP8=");
        await rerun.ACK.SendAsync(delivery);
        await rerun.ACK.SendAsync(delivery);
        await Rejected(events);
        rerun.StopProduction(); await rerun.Completion.WaitAsync(Deadline);
        publisher.Input.Complete(); await publisher.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Stop_before_start_completes_both_feedback_inputs()
    {
        await using var connection = new RabbitConnection(Factory());
        await using var consumer = new RabbitConsumer(connection, new() { Queue = "unused" });
        consumer.StopProduction();
        await consumer.Completion.WaitAsync(Deadline);
        (await consumer.ACK.SendAsync(Message(new { SettlementToken = "unused" }))).ShouldBeFalse();
        (await consumer.NACK.SendAsync(Message(new { SettlementToken = "unused" }))).ShouldBeFalse();
    }

    private static string Token(FlowMessage message) => message.Value.Json.GetProperty("SettlementToken").GetString()!;

    private static async Task<FlowEvent> Rejected(BufferBlock<FlowMessage> events)
    {
        while (true)
        {
            var message = await events.ReceiveAsync(Deadline);
            if (message.Value is FlowEvent { Name: "amqp.settlement_rejected" } rejection)
            {
                rejection.Level.ShouldBe(FlowEventLevel.Warning);
                return rejection;
            }
        }
    }

    private static async Task NoDelivery(BufferBlock<FlowMessage> output)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Should.ThrowAsync<OperationCanceledException>(() => output.ReceiveAsync(deadline.Token));
    }
}
