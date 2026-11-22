using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using RabbitMQ.Client;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.RabbitMQ.Tests;

public sealed partial class RabbitDeliveryTests
{
    [Fact]
    public async Task Publisher_distinguishes_mandatory_returns_from_nonmandatory_confirms_and_recovers_after_channel_error()
    {
        await using var connection = new RabbitConnection(Factory());
        await using var publisher = new RabbitPublisher(connection, new() { Queues = [new("outcomes")] });
        var output = Capture(publisher.Output);
        var request = new AmqpPublish { RoutingKey = "outcomes", Body = "AP8BgA==", Properties = new() { MessageId = "original-id" } };
        await Publish(request, false);
        await Publish(request with { RoutingKey = "absent", Mandatory = true }, true);
        await Publish(request with { RoutingKey = "absent", Mandatory = false }, false);
        var missingExchange = await Publish(request with { Exchange = "absent-exchange" }, true);
        missingExchange.Error!.Message.ShouldContain("NOT_FOUND");
        // A channel-level protocol error must not poison later, independently submitted requests.
        await Publish(request, false);
        publisher.Complete();
        await publisher.Completion.WaitAsync(Deadline);
        await using var inspection = await connection.OpenChannelAsync(false, CancellationToken.None);
        (await inspection.QueueDeclarePassiveAsync("outcomes")).MessageCount.ShouldBe(2u,
            "Only the two independently submitted, routable requests should be delivered.");

        async Task<FlowMessage> Publish(AmqpPublish publication, bool error)
        {
            var input = FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(publication)),
                correlationId: CorrelationId.New(), headers: new Dictionary<string, FlowValue> { ["original"] = new JsonFlowValue(JsonSerializer.SerializeToElement("retained")) });
            var original = input.Value.Json.GetRawText();
            (await publisher.Input.SendAsync(input)).ShouldBeTrue();
            var result = await output.ReceiveAsync(Deadline);
            result.IsError.ShouldBe(error, result.Error?.Message);
            result.CausationId.ShouldBe(input.MessageId);
            result.CorrelationId.ShouldBe(input.CorrelationId);
            result.Headers["original"].Json.GetString().ShouldBe("retained");
            input.Value.Json.GetRawText().ShouldBe(original);
            if (!error)
            {
                result.Value.Json.GetProperty("Status").GetString().ShouldBe("Confirmed");
                result.Value.Json.GetProperty("MessageId").GetString().ShouldBe("original-id");
            }
            return result;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publisher_request_during_native_recovery_is_not_replayed_and_Stop_does_not_restore_closed_channels(bool stopDuringRecovery)
    {
        var factory = Factory();
        factory.NetworkRecoveryInterval = TimeSpan.FromSeconds(8);
        await using var connection = new RabbitConnection(factory);
        await using var observer = await connection.OpenChannelAsync(false, CancellationToken.None);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.ChannelShutdownAsync += (_, _) => { disconnected.TrySetResult(); return Task.CompletedTask; };
        ((IRecoverable)observer).RecoveryAsync += (_, _) => { recovered.TrySetResult(); return Task.CompletedTask; };
        await using var publisher = new RabbitPublisher(connection, new() { Queues = [new("recovery-outcomes")] });
        var output = Capture(publisher.Output);
        var request = new AmqpPublish { RoutingKey = "recovery-outcomes", Body = "AP8=", Properties = new() { MessageId = "before" } };
        await publisher.Input.SendAsync(Message(request));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();

        var close = _broker.ExecAsync(["rabbitmqctl", "close_all_connections", "--global", "publisher recovery review"]);
        await disconnected.Task.WaitAsync(Deadline);
        var during = Message(request with { Properties = new() { MessageId = "during" } });
        await publisher.Input.SendAsync(during);
        var failure = await output.ReceiveAsync(Deadline);
        failure.Error.ShouldNotBeNull();
        failure.CausationId.ShouldBe(during.MessageId);
        recovered.Task.IsCompleted.ShouldBeFalse("The request must exercise the disconnected interval.");
        if (stopDuringRecovery)
        {
            publisher.Complete();
            await publisher.Completion.WaitAsync(Deadline);
        }
        (await close).ExitCode.ShouldBe(0);
        await recovered.Task.WaitAsync(Deadline);

        if (stopDuringRecovery)
        {
            (await publisher.Input.SendAsync(Message(request))).ShouldBeFalse();
            await using var next = new RabbitPublisher(connection);
            var nextOutput = Capture(next.Output);
            await next.Input.SendAsync(Message(request with { Properties = new() { MessageId = "after" } }));
            (await nextOutput.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
            next.Complete();
            await next.Completion.WaitAsync(Deadline);
        }
        else
        {
            await publisher.Input.SendAsync(Message(request with { Properties = new() { MessageId = "after" } }));
            (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
            publisher.Complete();
            await publisher.Completion.WaitAsync(Deadline);
        }
        output.Count.ShouldBe(0, "Neither recovery nor Stop should emit a second result for an earlier request.");
        (await observer.BasicGetAsync("recovery-outcomes", true))!.BasicProperties.MessageId.ShouldBe("before");
        (await observer.BasicGetAsync("recovery-outcomes", true))!.BasicProperties.MessageId.ShouldBe("after");
        (await observer.BasicGetAsync("recovery-outcomes", true)).ShouldBeNull("Failed requests must not be replayed automatically.");
        var channels = await _broker.ExecAsync(["rabbitmqctl", "list_channels", "--silent", "--no-table-headers", "number"]);
        channels.ExitCode.ShouldBe(0);
        channels.Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(1,
            "Only the observer channel should remain after the publisher stops and recovery finishes.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publisher_outstanding_confirmation_fails_on_disconnect_or_timeout_and_fresh_run_is_isolated(bool disconnect)
    {
        var factory = Factory();
        factory.AutomaticRecoveryEnabled = false;
        await using var connection = new RabbitConnection(factory);
        await using var publisher = new RabbitPublisher(connection, new() { Queues = [new("pending")] }, timeoutSeconds: 10);
        var output = Capture(publisher.Output);
        var request = new AmqpPublish { RoutingKey = "pending", Body = "AP8=" };
        await publisher.Input.SendAsync(Message(request));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        (await _broker.ExecAsync(["rabbitmqctl", "set_vm_memory_high_watermark", "absolute", "1"])).ExitCode.ShouldBe(0);
        try
        {
            await publisher.Input.SendAsync(Message(request));
            await WaitForBlockedPublisher();
            output.Count.ShouldBe(0, "A blocked publication has not received a broker confirmation.");
            publisher.Complete(); // Graceful Stop drains accepted input, bounded by the existing publish timeout.
            if (disconnect)
                (await _broker.ExecAsync(["rabbitmqctl", "close_all_connections", "--global", "publisher outcome trial"])).ExitCode.ShouldBe(0);
            var failed = await output.ReceiveAsync(Deadline);
            failed.Error.ShouldNotBeNull();
            // Native channel disposal has its own continuation timeout when the broker is blocked.
            await publisher.Completion.WaitAsync(TimeSpan.FromSeconds(45));
            output.Count.ShouldBe(0);
            (await publisher.Input.SendAsync(Message(request))).ShouldBeFalse();
        }
        finally
        {
            (await _broker.ExecAsync(["rabbitmqctl", "set_vm_memory_high_watermark", "0.4"])).ExitCode.ShouldBe(0);
        }
        await connection.DisposeAsync();
        await using var nextConnection = new RabbitConnection(Factory());
        await using var next = new RabbitPublisher(nextConnection);
        var nextOutput = Capture(next.Output);
        await next.Input.SendAsync(Message(request));
        (await nextOutput.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        next.Complete();
        await next.Completion.WaitAsync(Deadline);
        output.Count.ShouldBe(0, "The completed run must not receive a late confirmation from the next run.");
    }

    [Fact]
    public async Task Publisher_Stop_drains_accepted_requests_and_closes_its_channel()
    {
        await using var connection = new RabbitConnection(Factory());
        await using var publisher = new RabbitPublisher(connection, new() { Queues = [new("drain")] }, capacity: 8);
        var output = Capture(publisher.Output);
        var inputs = Enumerable.Range(0, 8).Select(index => Message(new AmqpPublish
            { RoutingKey = "drain", Body = "AA==", Properties = new() { MessageId = index.ToString() } })).ToArray();
        foreach (var input in inputs) (await publisher.Input.SendAsync(input)).ShouldBeTrue();
        publisher.Complete();
        await publisher.Completion.WaitAsync(Deadline);
        var results = new List<FlowMessage>();
        for (var index = 0; index < inputs.Length; index++) results.Add(await output.ReceiveAsync(Deadline));
        results.ShouldAllBe(result => !result.IsError);
        results.Select(result => result.CausationId).ShouldBe(inputs.Select(input => (MessageId?)input.MessageId));
        await using var inspection = await connection.OpenChannelAsync(false, CancellationToken.None);
        (await inspection.QueueDeclarePassiveAsync("drain")).MessageCount.ShouldBe(8u);
        (await _broker.ExecAsync(["rabbitmqctl", "list_channels", "--silent", "--no-table-headers", "number"])).Stdout.Trim().ShouldBe("1");
    }

    private async Task WaitForBlockedPublisher()
    {
        using var deadline = new CancellationTokenSource(Deadline);
        while (true)
        {
            var state = await _broker.ExecAsync(["rabbitmqctl", "list_connections", "--silent", "--no-table-headers", "state"], deadline.Token);
            state.ExitCode.ShouldBe(0);
            if (state.Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Contains("blocked")) return;
            await Task.Delay(50, deadline.Token);
        }
    }
}
