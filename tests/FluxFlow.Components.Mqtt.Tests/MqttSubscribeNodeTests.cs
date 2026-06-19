using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Nodes;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttSubscribeNodeTests
{
    [Fact]
    public async Task SubscribeNode_ReportsNotConnectedAndProducesNoMessages()
    {
        await using var connection = MqttTestContext.CreateConnection(new ThrowingMqttClientFactory());
        await using var node = new MqttSubscribeNode(
            connection,
            new MqttSubscriptionOptions
            {
                TopicFilter = "devices/+",
                QualityOfService = MqttQualityOfService.AtLeastOnce,
                ReceiveRetainedMessages = false,
                RetainAsPublished = true,
                BoundedCapacity = 4
            });

        var messages = MqttTestContext.Sink(node.Output, propagateCompletion: true);
        var errors = MqttTestContext.Sink(node.Errors);

        await node.StartAsync();

        // Before any connect the loop reports not connected once and produces nothing.
        var flowError = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        flowError.Code.ShouldBe(MqttErrorCodes.SubscribeNotConnected);
        flowError.Context.ShouldNotBeNull();
        flowError.Context.ShouldContain("topicFilter=devices/+");
        flowError.Context.ShouldContain($"connectionName={MqttTestContext.ConnectionName}");

        // No client => no messages, but Complete() still completes cleanly.
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        messages.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task SubscribeNode_SubscribesAfterConnectAndFlowsMessages()
    {
        var adapter = new RecordingMqttClientAdapter();
        var factory = new RecordingMqttClientFactory(adapter, ownLease: false);
        await using var connection = MqttTestContext.CreateConnection(factory);
        await using var node = new MqttSubscribeNode(
            connection,
            new MqttSubscriptionOptions { TopicFilter = "devices/+" });

        var messages = MqttTestContext.Sink(node.Output);

        // Before connect: nothing flows.
        await node.StartAsync();
        messages.TryReceive(out _).ShouldBeFalse();
        adapter.SubscribeCalls.ShouldBe(0);

        // After ConnectAsync (post-start): subscription opens.
        await connection.ConnectAsync();
        await adapter.Subscribed.WaitAsync(TimeSpan.FromSeconds(5));
        adapter.SubscribeCalls.ShouldBe(1);

        adapter.PushMessage(CreateMessage("devices/a", "m1"));
        var received = await messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        received.Payload.Topic.ShouldBe("devices/a");
        received.Payload.CorrelationId.ShouldBe("m1");
        // The adapter-supplied correlation id flows onto the envelope.
        received.CorrelationId.ShouldBe(new CorrelationId("m1"));

        // DisconnectAsync: the subscription is disposed.
        var subscription = adapter.Subscriptions[0];
        await connection.DisconnectAsync();
        await WaitForAsync(() => subscription.Disposed, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SubscribeNode_ResubscribesOnReconnectWithNewEpoch()
    {
        // A fresh adapter per connect models a genuine reconnect (new lease/epoch).
        var adapters = new List<RecordingMqttClientAdapter>();
        var factory = new RecordingMqttClientFactory(
            _ =>
            {
                var created = new RecordingMqttClientAdapter();
                adapters.Add(created);
                return created;
            });
        await using var connection = MqttTestContext.CreateConnection(factory);
        await using var node = new MqttSubscribeNode(
            connection,
            new MqttSubscriptionOptions { TopicFilter = "devices/+" });

        var messages = MqttTestContext.Sink(node.Output);
        await node.StartAsync();

        // First lease (epoch 1).
        await connection.ConnectAsync();
        var firstEpoch = connection.ConnectionEpoch;
        await adapters[0].Subscribed.WaitAsync(TimeSpan.FromSeconds(5));
        adapters[0].SubscribeCalls.ShouldBe(1);
        adapters[0].PushMessage(CreateMessage("devices/a", "first"));
        (await messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .Payload.CorrelationId.ShouldBe("first");

        // Reconnect (Disconnect + Connect => new lease/epoch).
        await connection.DisconnectAsync();
        await connection.ConnectAsync();
        connection.ConnectionEpoch.ShouldBeGreaterThan(firstEpoch);
        await adapters[1].Subscribed.WaitAsync(TimeSpan.FromSeconds(5));
        adapters[1].SubscribeCalls.ShouldBe(1);

        adapters[1].PushMessage(CreateMessage("devices/b", "second"));
        (await messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .Payload.CorrelationId.ShouldBe("second");
    }

    [Fact]
    public async Task SubscribeNode_DoesNotDoubleSubscribeWithinSameLeaseEpoch()
    {
        var adapter = new RecordingMqttClientAdapter();
        var factory = new RecordingMqttClientFactory(adapter, ownLease: false);
        await using var connection = MqttTestContext.CreateConnection(factory);
        await using var node = new MqttSubscribeNode(
            connection,
            new MqttSubscriptionOptions { TopicFilter = "devices/+" });

        var messages = MqttTestContext.Sink(node.Output);
        await node.StartAsync();

        await connection.ConnectAsync();
        await adapter.Subscribed.WaitAsync(TimeSpan.FromSeconds(5));
        adapter.SubscribeCalls.ShouldBe(1);

        // A within-lease Reconnecting -> Connected health blip on the SAME lease
        // (same epoch) must not resubscribe.
        adapter.PushHealth(new MqttClientHealthEvent { State = MqttClientHealthState.Reconnecting });
        adapter.PushHealth(new MqttClientHealthEvent { State = MqttClientHealthState.Connected });

        // Drive a message through to prove the original subscription is still pumping.
        adapter.PushMessage(CreateMessage("devices/a", "still-here"));
        (await messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .Payload.CorrelationId.ShouldBe("still-here");

        // The connection epoch never advanced and SubscribeAsync stayed at one.
        connection.ConnectionEpoch.ShouldBe(1);
        adapter.SubscribeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task SubscribeNode_CompletesCleanlyOnComplete()
    {
        await using var connection = MqttTestContext.CreateConnection(new ThrowingMqttClientFactory());
        await using var node = new MqttSubscribeNode(
            connection,
            new MqttSubscriptionOptions { TopicFilter = "devices/+" });

        await node.StartAsync();
        node.Completion.IsCompleted.ShouldBeFalse();

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task SubscribeNode_DisposeCompletesNode()
    {
        await using var connection = MqttTestContext.CreateConnection(new ThrowingMqttClientFactory());
        var node = new MqttSubscribeNode(
            connection,
            new MqttSubscriptionOptions { TopicFilter = "devices/#" });

        await node.StartAsync();
        await node.DisposeAsync();

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public void SubscribeNode_RejectsNullConnection()
        => Should.Throw<ArgumentNullException>(() => new MqttSubscribeNode(connection: null!));

    private static MqttReceivedMessage CreateMessage(string topic, string correlationId)
        => new()
        {
            Timestamp = DateTimeOffset.UtcNow,
            Topic = topic,
            Payload = [1, 2, 3],
            CorrelationId = correlationId
        };

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }
}
