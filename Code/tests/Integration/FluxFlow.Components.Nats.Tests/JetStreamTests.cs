using System.Diagnostics;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks.Dataflow;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluxFlow.Components.Nats.JetStream;
using FluxFlow.Core.Messaging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Nats.Tests;

public sealed partial class JetStreamTests : IAsyncLifetime
{
    private readonly IContainer _broker = new ContainerBuilder("nats:2.11.8-alpine")
        .WithResourceMapping(Encoding.UTF8.GetBytes("""
            port: 4222
            jetstream { store_dir: /tmp/jetstream }
            authorization { users: [
              { user: trial, password: trial-password },
              { user: existing, password: trial-password, permissions: {
                publish: ["events.>", "$JS.ACK.>", "$JS.API.CONSUMER.INFO.TRIAL.worker", "$JS.API.CONSUMER.MSG.NEXT.TRIAL.worker"], subscribe: ["_INBOX.>"]
              } }
            ] }
            """), "/etc/nats/trial.conf").WithCommand("-c", "/etc/nats/trial.conf")
        .WithPortBinding(4222, true).WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready")).Build();
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    public Task InitializeAsync() => _broker.StartAsync();
    public async Task DisposeAsync() => await _broker.DisposeAsync();
    private NatsOpts Options => NatsOpts.Default with { Url = $"nats://{_broker.Hostname}:{_broker.GetMappedPublicPort(4222)}",
        AuthOpts = new() { Username = "trial", Password = "trial-password" },
        ConnectTimeout = TimeSpan.FromSeconds(2), RequestTimeout = TimeSpan.FromSeconds(2), CommandTimeout = TimeSpan.FromSeconds(2),
        MaxReconnectRetry = 2, ReconnectWaitMin = TimeSpan.FromMilliseconds(100), ReconnectWaitMax = TimeSpan.FromMilliseconds(100), DrainSubscriptionsOnDispose = false };
    private static JetStreamOptions Stream => new() { Stream = "TRIAL", StreamSubject = "events.>", CreateStream = true, Limits = new() { Capacity = 2, TimeoutSeconds = 2 } };
    private static JetStreamConsumerOptions Consumer => new() { Stream = Stream, Consumer = "worker", CreateConsumer = true, AckWaitSeconds = 1, MaxDeliver = 3, MaxInFlight = 2 };
    private static FlowMessage Value<T>(T value) => FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(value)));
    private static BufferBlock<FlowMessage> Capture(ISourceBlock<FlowMessage> source)
    { var target = new BufferBlock<FlowMessage>(); source.LinkTo(target); return target; }
    private static async Task<JetStreamDelivery> Receive(BufferBlock<FlowMessage> messages)
    {
        var message = await messages.ReceiveAsync(Deadline); message.Error.ShouldBeNull();
        return message.Value.Json.Deserialize<JetStreamDelivery>()!;
    }
    private static NatsMessage Payload => new() { Subject = "events.one", DataBase64 = "AP8BgA==", Headers = new() { ["Correlation-Id"] = ["one", "two"] } };

    [Fact]
    public async Task Confirmed_publication_preserves_bytes_headers_metadata_and_explicit_settlement()
    {
        await using var session = new NatsSession(Options);
        await using var consumer = new JetStreamConsumer(session, Consumer);
        var received = Capture(consumer.Output); await consumer.StartAsync().WaitAsync(Deadline);
        await using var publisher = new JetStreamPublisher(session, Stream); var confirmed = Capture(publisher.Output);
        await using var settlement = new JetStreamSettlementNode(consumer); var settled = Capture(settlement.Output);
        for (var i = 0; i < 30; i++)
        {
            await publisher.Input.SendAsync(Value(Payload));
            var response = await confirmed.ReceiveAsync(Deadline); response.Error.ShouldBeNull();
            var ack = response.Value.Json.Deserialize<JetStreamPublished>()!;
            ack.Stream.ShouldBe("TRIAL"); ack.Sequence.ShouldBe((ulong)i + 1); ack.Duplicate.ShouldBeFalse();
            var delivery = await Receive(received);
            delivery.Message.DataBase64.ShouldBe(Payload.DataBase64); delivery.Message.Headers["Correlation-Id"].ShouldBe(new[] { "one", "two" });
            delivery.Metadata.Sequence.Stream.ShouldBe(ack.Sequence); delivery.Metadata.NumDelivered.ShouldBe(1UL);
            await settlement.Input.SendAsync(Value(new JetStreamSettlement(delivery.SettlementToken)));
            (await settled.ReceiveAsync(Deadline)).Error.ShouldBeNull();
        }
        var context = new NatsJSContext(await session.OpenAsync(CancellationToken.None));
        (await context.GetConsumerAsync("TRIAL", "worker")).Info.NumAckPending.ShouldBe(0);
        consumer.StopProduction(); await consumer.ProductionCompletion.WaitAsync(Deadline);
        await consumer.DisposeAsync();
        (await context.GetStreamAsync("TRIAL")).Info.State.Messages.ShouldBe(30L);
    }

    [Fact]
    public async Task Published_message_redelivers_after_delayed_nak_and_only_the_new_run_can_ack_it()
    {
        var options = Consumer with { MaxInFlight = 1, AckWaitSeconds = 5 };
        JetStreamDelivery second;
        await using (var session = new NatsSession(Options))
        {
            await using var consumer = new JetStreamConsumer(session, options);
            var received = Capture(consumer.Output);
            await consumer.StartAsync().WaitAsync(Deadline);
            await using var publisher = new JetStreamPublisher(session, Stream);
            var published = Capture(publisher.Output);
            await using var commands = new JetStreamSettlementNode(consumer);
            var replies = Capture(commands.Output);

            (await publisher.Input.SendAsync(Value(Payload)).WaitAsync(Deadline)).ShouldBeTrue();
            var confirmation = await published.ReceiveAsync(Deadline);
            confirmation.Error.ShouldBeNull();
            var sequence = confirmation.Value.Json.Deserialize<JetStreamPublished>()!.Sequence;
            var first = await Receive(received);
            first.Metadata.Sequence.Stream.ShouldBe(sequence);
            first.Metadata.NumDelivered.ShouldBe(1UL);
            var delay = Stopwatch.StartNew();
            await SendSettlement(commands, replies, new JetStreamSettlement(first.SettlementToken, "Nak", 2));
            second = await Receive(received);
            delay.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2));
            second.Metadata.Sequence.Stream.ShouldBe(sequence);
            second.Metadata.NumDelivered.ShouldBe(2UL);
            second.SettlementToken.ShouldNotBe(first.SettlementToken);
            await SendSettlement(commands, replies, new JetStreamSettlement(first.SettlementToken), rejected: true);
            consumer.StopProduction();
            await consumer.ProductionCompletion.WaitAsync(Deadline);
        }
        await using var nextSession = new NatsSession(Options);
        await using var rerun = new JetStreamConsumer(nextSession, options with { CreateConsumer = false, Stream = Stream with { CreateStream = false } });
        var next = Capture(rerun.Output); await rerun.StartAsync().WaitAsync(Deadline);
        var redelivery = await Receive(next); redelivery.Metadata.NumDelivered.ShouldBe(3UL);
        redelivery.Metadata.Sequence.Stream.ShouldBe(second.Metadata.Sequence.Stream);
        redelivery.Message.DataBase64.ShouldBe(Payload.DataBase64);
        redelivery.Message.Headers["Correlation-Id"].ShouldBe(Payload.Headers["Correlation-Id"]);
        redelivery.SettlementToken.ShouldNotBe(second.SettlementToken);
        await using var settlement = new JetStreamSettlementNode(rerun);
        var results = Capture(settlement.Output);
        var saved = new NatsJSContext(await nextSession.OpenAsync(default));
        foreach (var action in new[] { "Ack", "Nak", "Progress", "Terminate" })
            await SendSettlement(settlement, results, new JetStreamSettlement(second.SettlementToken, action), rejected: true);
        (await saved.GetConsumerAsync("TRIAL", "worker")).Info.NumAckPending.ShouldBe(1);
        await SendSettlement(settlement, results, new JetStreamSettlement(redelivery.SettlementToken));
        (await saved.GetConsumerAsync("TRIAL", "worker")).Info.NumAckPending.ShouldBe(0);
        (await saved.GetStreamAsync("TRIAL")).Info.State.Messages.ShouldBe(1L);
    }

    [Fact]
    public async Task In_flight_limit_stops_fetching_and_stop_keeps_terminal_ack_available_during_drain()
    {
        await using var session = new NatsSession(Options);
        await using var consumer = new JetStreamConsumer(session, Consumer with { MaxInFlight = 1, AckWaitSeconds = 30 });
        var received = Capture(consumer.Output); await consumer.StartAsync();
        var context = new NatsJSContext(await session.OpenAsync(CancellationToken.None));
        for (var i = 0; i < 5; i++) (await context.PublishAsync("events.one", new byte[] { (byte)i })).EnsureSuccess();
        var first = await Receive(received); await Task.Delay(200); received.Count.ShouldBe(0);
        (await context.GetConsumerAsync("TRIAL", "worker")).Info.NumAckPending.ShouldBe(1);
        consumer.StopProduction(); await consumer.ProductionCompletion.WaitAsync(Deadline);
        await consumer.SettleAsync(new(first.SettlementToken));
        consumer.Complete(); await consumer.Completion.WaitAsync(Deadline);
        await Should.ThrowAsync<OperationCanceledException>(() => consumer.SettleAsync(new(first.SettlementToken)));
        (await context.GetConsumerAsync("TRIAL", "worker")).Info.NumAckPending.ShouldBe(0);
        (await context.GetStreamAsync("TRIAL")).Info.State.Messages.ShouldBe(5L);
    }

    [Fact]
    public async Task Provisioning_is_opt_in_and_never_updates_existing_stream_or_consumer()
    {
        await using var session = new NatsSession(Options);
        await using (var missing = new JetStreamConsumer(session, Consumer with { CreateConsumer = false, Stream = Stream with { CreateStream = false } }))
            await Should.ThrowAsync<NatsJSApiException>(() => missing.StartAsync().WaitAsync(Deadline));
        var context = new NatsJSContext(await session.OpenAsync(CancellationToken.None));
        await Should.ThrowAsync<NatsJSApiException>(() => context.GetStreamAsync("TRIAL").AsTask());
        await context.CreateStreamAsync(new StreamConfig("TRIAL", ["events.>"]) { MaxMsgs = 7 });
        await context.CreateConsumerAsync("TRIAL", new ConsumerConfig("worker") { AckPolicy = ConsumerConfigAckPolicy.Explicit, AckWait = TimeSpan.FromSeconds(19), MaxDeliver = 9 });
        await using var consumer = new JetStreamConsumer(session, Consumer); consumer.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage>());
        await consumer.StartAsync();
        (await context.GetStreamAsync("TRIAL")).Info.Config.MaxMsgs.ShouldBe(7);
        (await context.GetConsumerAsync("TRIAL", "worker")).Info.Config.AckWait.ShouldBe(TimeSpan.FromSeconds(19));
        await consumer.DisposeAsync();
        (await context.GetConsumerAsync("TRIAL", "worker")).Info.Config.MaxDeliver.ShouldBe(9);
    }

    [Fact]
    public async Task Publish_missing_stream_errors_and_message_id_deduplication_is_visible()
    {
        await using var session = new NatsSession(Options);
        await using var missing = new JetStreamPublisher(session, Stream with { CreateStream = false }); var errors = Capture(missing.Output);
        await missing.Input.SendAsync(Value(Payload)); (await errors.ReceiveAsync(Deadline)).Error.ShouldNotBeNull();
        await using var publisher = new JetStreamPublisher(session, Stream); var confirmed = Capture(publisher.Output);
        var payload = Payload with { Headers = new() { ["Nats-Msg-Id"] = ["same-publication"] } };
        for (var i = 0; i < 2; i++)
        {
            await publisher.Input.SendAsync(Value(payload)); var response = await confirmed.ReceiveAsync(Deadline); response.Error.ShouldBeNull(JsonSerializer.Serialize(response.Error));
            var ack = response.Value.Json.Deserialize<JetStreamPublished>()!; ack.Sequence.ShouldBe(1UL); ack.Duplicate.ShouldBe(i == 1);
        }
        var context = new NatsJSContext(await session.OpenAsync(default));
        (await context.GetStreamAsync("TRIAL")).Info.State.Messages.ShouldBe(1L);
    }

    [Fact]
    public async Task Publisher_rejects_service_replies_that_do_not_acknowledge_a_stored_sequence_in_its_stream()
    {
        await using var session = new NatsSession(Options);
        var native = await session.OpenAsync(default);
        await using var service = await native.SubscribeCoreAsync<byte[]>("service");
        await native.PingAsync();
        await using var publisher = new JetStreamPublisher(session, Stream);
        var results = Capture(publisher.Output);
        using var deadline = new CancellationTokenSource(Deadline);
        foreach (var body in new[] { """{"stream":"OTHER","seq":1}""", """{"stream":"TRIAL","seq":0}""" })
        {
            var input = Value(Payload with { Subject = "service" });
            await publisher.Input.SendAsync(input, deadline.Token);
            var delivery = await service.Msgs.ReadAsync(deadline.Token);
            await delivery.ReplyAsync(Encoding.UTF8.GetBytes(body), cancellationToken: deadline.Token);
            var result = await results.ReceiveAsync(deadline.Token);
            result.IsError.ShouldBeTrue();
            result.CausationId.ShouldBe(input.MessageId);
        }
        var context = new NatsJSContext(native);
        (await context.GetStreamAsync("TRIAL")).Info.State.Messages.ShouldBe(0L);
        await publisher.Input.SendAsync(Value(Payload));
        (await results.ReceiveAsync(deadline.Token)).Value.Json.Deserialize<JetStreamPublished>()!.Sequence.ShouldBe(1UL);
    }

    [Theory]
    [InlineData("Ack")]
    [InlineData("Terminate")]
    public async Task Settling_redelivery_releases_all_attempts_and_restores_pull_capacity(string action)
    {
        await using var session = new NatsSession(Options);
        await using var consumer = new JetStreamConsumer(session, Consumer with { MaxInFlight = 2 });
        var received = Capture(consumer.Output);
        await consumer.StartAsync().WaitAsync(Deadline);
        var context = new NatsJSContext(await session.OpenAsync(default));
        (await context.PublishAsync("events.one", new byte[] { 1 })).EnsureSuccess();
        var first = await Receive(received);
        var redelivery = await Receive(received);
        redelivery.Metadata.Sequence.Stream.ShouldBe(first.Metadata.Sequence.Stream);
        redelivery.Metadata.NumDelivered.ShouldBe(2UL);
        await consumer.SettleAsync(new(redelivery.SettlementToken, action));
        if (action == "Terminate")
        {
            // Terminate is one-way; its local send can finish before the broker updates the durable.
            using var deadline = new CancellationTokenSource(Deadline);
            while ((await context.GetConsumerAsync("TRIAL", "worker", deadline.Token)).Info.NumAckPending != 0)
                await Task.Delay(25, deadline.Token);
        }
        (await context.GetConsumerAsync("TRIAL", "worker")).Info.NumAckPending.ShouldBe(0);

        // Both slots must now be available for different, still-unsettled messages.
        var b = await context.PublishAsync("events.two", new byte[] { 2 });
        var c = await context.PublishAsync("events.three", new byte[] { 3 });
        (await Receive(received)).Metadata.Sequence.Stream.ShouldBe(b.Seq);
        (await Receive(received)).Metadata.Sequence.Stream.ShouldBe(c.Seq);
        await Should.ThrowAsync<InvalidOperationException>(() => consumer.SettleAsync(new(first.SettlementToken)));
    }

    [Fact]
    public async Task Outstanding_pull_cancels_and_broker_loss_faults_production()
    {
        await using var session = new NatsSession(Options);
        await using (var cancelled = new JetStreamConsumer(session, Consumer))
        {
            cancelled.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage>()); await cancelled.StartAsync();
            cancelled.StopProduction(); await cancelled.ProductionCompletion.WaitAsync(Deadline);
        }
        await using var consumer = new JetStreamConsumer(session, Consumer); consumer.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage>());
        await consumer.StartAsync(); await _broker.StopAsync();
        await Should.ThrowAsync<Exception>(() => consumer.ProductionCompletion.WaitAsync(Deadline));
    }

    [Fact]
    public async Task Existing_entities_work_without_provisioning_permissions_and_forbidden_creation_fails()
    {
        await using var admin = new NatsSession(Options);
        await using (var provision = new JetStreamConsumer(admin, Consumer))
        { provision.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage>()); await provision.StartAsync(); }
        var restrictedOptions = Options with { AuthOpts = new() { Username = "existing", Password = "trial-password" } };
        await using (var session = new NatsSession(restrictedOptions))
        {
            await using var consumer = new JetStreamConsumer(session, Consumer with { CreateConsumer = false, Stream = Stream with { CreateStream = false } });
            var deliveries = Capture(consumer.Output); await consumer.StartAsync();
            await using var publisher = new JetStreamPublisher(session, Stream with { CreateStream = false }); var confirmed = Capture(publisher.Output);
            await publisher.Input.SendAsync(Value(Payload)); (await confirmed.ReceiveAsync(Deadline)).Error.ShouldBeNull();
            var delivery = await Receive(deliveries); await consumer.SettleAsync(new(delivery.SettlementToken));
        }
        await using var denied = new NatsSession(restrictedOptions);
        await using var creator = new JetStreamConsumer(denied, Consumer with { Consumer = "forbidden" });
        creator.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage>());
        await Should.ThrowAsync<Exception>(() => creator.StartAsync().WaitAsync(Deadline));
    }

    [Fact]
    public async Task Broker_delivery_limit_is_respected_and_terminate_does_not_ack_success()
    {
        await using var session = new NatsSession(Options);
        await using var consumer = new JetStreamConsumer(session, Consumer with { MaxInFlight = 4, MaxDeliver = 2 });
        var received = Capture(consumer.Output); await consumer.StartAsync();
        var context = new NatsJSContext(await session.OpenAsync(CancellationToken.None));
        (await context.PublishAsync("events.one", new byte[] { 1 })).EnsureSuccess();
        var first = await Receive(received); var second = await Receive(received);
        second.Metadata.NumDelivered.ShouldBe(2UL);
        await Task.Delay(1500); received.Count.ShouldBe(0);
        await consumer.SettleAsync(new(second.SettlementToken, "Terminate"));
        (await context.GetStreamAsync("TRIAL")).Info.State.Messages.ShouldBe(1L);
        first.Metadata.Sequence.Stream.ShouldBe(second.Metadata.Sequence.Stream);
    }
}
