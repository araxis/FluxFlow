using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluxFlow.Components.RabbitMQ;
using FluxFlow.Core.Messaging;
using RabbitMQ.Client;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.RabbitMQ.Tests;

public sealed partial class RabbitDeliveryTests : IAsyncLifetime
{
    private readonly IContainer _broker = new ContainerBuilder("rabbitmq:4.1.0-management")
        .WithEnvironment("RABBITMQ_DEFAULT_USER", "trial")
        .WithEnvironment("RABBITMQ_DEFAULT_PASS", "trial-password")
        .WithPortBinding(5672, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete")).Build();
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);
    public Task InitializeAsync() => _broker.StartAsync();
    public async Task DisposeAsync() => await _broker.DisposeAsync();
    private ConnectionFactory Factory() => new()
    {
        HostName = _broker.Hostname, Port = _broker.GetMappedPublicPort(5672), UserName = "trial", Password = "trial-password",
        AutomaticRecoveryEnabled = true, NetworkRecoveryInterval = TimeSpan.FromSeconds(1)
    };
    private static FlowMessage Message<T>(T value) => FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(value)));
    private static BufferBlock<FlowMessage> Capture(ISourceBlock<FlowMessage> output)
    {
        var buffer = new BufferBlock<FlowMessage>(); output.LinkTo(buffer); return buffer;
    }

    [Fact]
    public async Task Confirmed_delivery_preserves_bytes_metadata_and_requires_explicit_settlement()
    {
        await using var connection = new RabbitConnection(Factory());
        await using var consumer = new RabbitConsumer(connection, new()
        {
            Queue = "delivery", Prefetch = 1, Capacity = 1,
            Topology = new() { Queues = [new("delivery")], Exchanges = [new("events", "topic")], Bindings = [new("delivery", "events", "sensor.*")] }
        });
        var received = Capture(consumer.Output);
        await consumer.StartAsync().WaitAsync(Deadline);
        await using var publisher = new RabbitPublisher(connection);
        var confirmed = Capture(publisher.Output);
        var request = new AmqpPublish
        {
            Exchange = "events", RoutingKey = "sensor.one", Body = "AP8BgA==",
            Properties = new() { MessageId = "message-one", CorrelationId = "correlation-one", ContentType = "application/octet-stream",
                Headers = new() { ["binary"] = AmqpWireField.FromNative(new byte[] { 0, 255 }), ["large"] = AmqpWireField.FromNative(long.MaxValue) } }
        };
        (await publisher.Input.SendAsync(Message(request))).ShouldBeTrue();
        (await confirmed.ReceiveAsync(Deadline)).Error.ShouldBeNull();
        var first = (await received.ReceiveAsync(Deadline)).Value.Json.Deserialize<AmqpDelivery>()!;
        first.Body.ShouldBe(request.Body); first.Properties.MessageId.ShouldBe("message-one");
        first.Properties.CorrelationId.ShouldBe("correlation-one");
        ((byte[])first.Properties.Headers!["binary"].ToNative()!).ShouldBe(new byte[] { 0, 255 });
        first.Properties.Headers["large"].ToNative().ShouldBe(long.MaxValue);
        await publisher.Input.SendAsync(Message(request with { Properties = new() { MessageId = "message-two" } }));
        await confirmed.ReceiveAsync(Deadline);
        using (var probe = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
            await Should.ThrowAsync<OperationCanceledException>(() => received.ReceiveAsync(probe.Token));
        await consumer.SettleAsync(new() { SettlementToken = first.SettlementToken, Action = "Requeue" });
        var redelivery = (await received.ReceiveAsync(Deadline)).Value.Json.Deserialize<AmqpDelivery>()!;
        redelivery.Redelivered.ShouldBeTrue(); redelivery.Body.ShouldBe(first.Body);
        await Should.ThrowAsync<InvalidOperationException>(() => consumer.SettleAsync(new() { SettlementToken = first.SettlementToken }));
        await consumer.SettleAsync(new() { SettlementToken = redelivery.SettlementToken });
        var second = (await received.ReceiveAsync(Deadline)).Value.Json.Deserialize<AmqpDelivery>()!;
        second.Properties.MessageId.ShouldBe("message-two");
        await consumer.SettleAsync(new() { SettlementToken = second.SettlementToken, Action = "Reject" });
        consumer.StopProduction(); await consumer.Completion.WaitAsync(Deadline);
        await Should.ThrowAsync<InvalidOperationException>(() => consumer.SettleAsync(new() { SettlementToken = second.SettlementToken }));
        publisher.Input.Complete(); await publisher.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Mandatory_unroutable_publication_is_not_reported_as_confirmed()
    {
        await using var connection = new RabbitConnection(Factory());
        await using var publisher = new RabbitPublisher(connection);
        var output = Capture(publisher.Output);
        await publisher.Input.SendAsync(Message(new AmqpPublish { RoutingKey = "missing-queue", Body = "AA==" }));
        var failed = await output.ReceiveAsync(Deadline);
        failed.Error.ShouldNotBeNull();
        publisher.Input.Complete(); await publisher.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Stop_requeues_unsettled_delivery_and_new_run_rejects_old_token()
    {
        await using var connection = new RabbitConnection(Factory());
        string oldToken;
        await using (var first = new RabbitConsumer(connection, new() { Queue = "retained", Topology = new() { Queues = [new("retained")] } }))
        {
            var output = Capture(first.Output); await first.StartAsync().WaitAsync(Deadline);
            await using var publisher = new RabbitPublisher(connection);
            var confirmations = Capture(publisher.Output);
            await publisher.Input.SendAsync(Message(new AmqpPublish { RoutingKey = "retained", Body = "AQID" }));
            await confirmations.ReceiveAsync(Deadline);
            oldToken = (await output.ReceiveAsync(Deadline)).Value.Json.Deserialize<AmqpDelivery>()!.SettlementToken;
            first.StopProduction(); await first.Completion.WaitAsync(Deadline);
            publisher.Input.Complete(); await publisher.Completion.WaitAsync(Deadline);
        }
        await using var rerun = new RabbitConsumer(connection, new() { Queue = "retained" });
        var replay = Capture(rerun.Output); await rerun.StartAsync().WaitAsync(Deadline);
        var value = (await replay.ReceiveAsync(Deadline)).Value.Json.Deserialize<AmqpDelivery>()!;
        value.Redelivered.ShouldBeTrue(); value.Body.ShouldBe("AQID");
        await Should.ThrowAsync<InvalidOperationException>(() => rerun.SettleAsync(new() { SettlementToken = oldToken }));
        await rerun.SettleAsync(new() { SettlementToken = value.SettlementToken });
        rerun.StopProduction(); await rerun.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Existing_topology_works_without_declaration_privileges_and_repeated_delivery_stays_bounded()
    {
        await using var admin = await Factory().CreateConnectionAsync();
        await using var setup = await admin.CreateChannelAsync();
        await setup.QueueDeclareAsync("restricted", true, false, false);
        (await _broker.ExecAsync(["rabbitmqctl", "add_user", "restricted", "restricted-password"])).ExitCode.ShouldBe(0);
        (await _broker.ExecAsync(["rabbitmqctl", "set_permissions", "-p", "/", "restricted", "^$", "^(restricted|amq.default)$", "^restricted$"])).ExitCode.ShouldBe(0);
        var factory = Factory(); factory.UserName = "restricted"; factory.Password = "restricted-password";
        await using var connection = new RabbitConnection(factory);
        await using var consumer = new RabbitConsumer(connection, new() { Queue = "restricted", Prefetch = 2, Capacity = 2 });
        var output = Capture(consumer.Output); await consumer.StartAsync().WaitAsync(Deadline);
        await using var publisher = new RabbitPublisher(connection, capacity: 2);
        var confirms = Capture(publisher.Output);
        for (var index = 0; index < 40; index++)
        {
            await publisher.Input.SendAsync(Message(new AmqpPublish { RoutingKey = "restricted", Body = Convert.ToBase64String(BitConverter.GetBytes(index)) }));
            (await confirms.ReceiveAsync(Deadline)).Error.ShouldBeNull();
            var delivery = (await output.ReceiveAsync(Deadline)).Value.Json.Deserialize<AmqpDelivery>()!;
            BitConverter.ToInt32(Convert.FromBase64String(delivery.Body)).ShouldBe(index);
            await consumer.SettleAsync(new() { SettlementToken = delivery.SettlementToken });
        }
        consumer.StopProduction(); await consumer.Completion.WaitAsync(Deadline);
        publisher.Input.Complete(); await publisher.Completion.WaitAsync(Deadline);

        await using var forbidden = new RabbitConsumer(connection, new()
        {
            Queue = "restricted", Topology = new() { Queues = [new("forbidden-new-queue")] }
        });
        await Should.ThrowAsync<Exception>(() => forbidden.StartAsync().WaitAsync(Deadline));
        await Should.ThrowAsync<Exception>(() => forbidden.ProductionCompletion.WaitAsync(Deadline));
    }

    [Fact]
    public async Task Invalid_credentials_and_pre_cancelled_start_do_not_leave_a_consumer()
    {
        var factory = Factory(); factory.Password = "incorrect";
        await using var connection = new RabbitConnection(factory);
        await using var consumer = new RabbitConsumer(connection, new() { Queue = "absent" });
        await Should.ThrowAsync<Exception>(() => consumer.StartAsync().WaitAsync(Deadline));
        await Should.ThrowAsync<Exception>(() => consumer.ProductionCompletion.WaitAsync(Deadline));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await using var validConnection = new RabbitConnection(Factory());
        await Should.ThrowAsync<OperationCanceledException>(() => validConnection.OpenChannelAsync(false, cancelled.Token));
    }

    [Fact]
    public async Task Native_recovery_redelivers_and_expires_settlement_tokens()
    {
        await using var connection = new RabbitConnection(Factory());
        await using var consumer = new RabbitConsumer(connection, new() { Queue = "recover", Prefetch = 1, Topology = new() { Queues = [new("recover")] } });
        var output = Capture(consumer.Output); await consumer.StartAsync().WaitAsync(Deadline);
        await using var publisher = new RabbitPublisher(connection);
        var confirms = Capture(publisher.Output);
        await publisher.Input.SendAsync(Message(new AmqpPublish { RoutingKey = "recover", Body = "AP8=", Properties = new() { DeliveryMode = 2 } }));
        await confirms.ReceiveAsync(Deadline);
        var before = (await output.ReceiveAsync(Deadline)).Value.Json.Deserialize<AmqpDelivery>()!;
        (await _broker.ExecAsync(["rabbitmqctl", "close_all_connections", "--global", "recovery trial"])).ExitCode.ShouldBe(0);
        var next = output.ReceiveAsync(TimeSpan.FromSeconds(45));
        if (await Task.WhenAny(next, consumer.ProductionCompletion) == consumer.ProductionCompletion)
            await consumer.ProductionCompletion;
        var after = (await next).Value.Json.Deserialize<AmqpDelivery>()!;
        after.Redelivered.ShouldBeTrue(); after.Body.ShouldBe(before.Body);
        await using var acknowledge = new RabbitSettlement(consumer);
        var results = Capture(acknowledge.Output);
        var stale = Message(new { before.SettlementToken });
        (await acknowledge.Input.SendAsync(stale)).ShouldBeTrue();
        var refused = await results.ReceiveAsync(Deadline);
        refused.Error.ShouldNotBeNull().Message.ShouldContain("already settled");
        refused.CausationId.ShouldBe(stale.MessageId);
        var events = Capture(consumer.Events);
        await consumer.ACK.SendAsync(Message(new { SettlementToken = before.SettlementToken }));
        await Rejected(events);
        await consumer.ACK.SendAsync(Message(new { SettlementToken = after.SettlementToken }));
        await consumer.ACK.SendAsync(Message(new { SettlementToken = after.SettlementToken }));
        await Rejected(events);
        await publisher.Input.SendAsync(Message(new AmqpPublish { RoutingKey = "recover", Body = "AQI=" }));
        (await confirms.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        var fresh = (await output.ReceiveAsync(Deadline)).Value.Json.Deserialize<AmqpDelivery>()!;
        fresh.Body.ShouldBe("AQI=");
        fresh.Redelivered.ShouldBeFalse();
        var current = Message(new { fresh.SettlementToken });
        (await acknowledge.Input.SendAsync(current)).ShouldBeTrue();
        (await results.ReceiveAsync(Deadline)).ShouldBeSameAs(current);
        consumer.StopProduction(); await consumer.Completion.WaitAsync(Deadline);
        publisher.Input.Complete(); await publisher.Completion.WaitAsync(Deadline);
    }
}
