using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttSubscribeNodeTests
{
    [Fact]
    public async Task SubscribeNode_ReportsNotConnectedAndProducesNoMessages()
    {
        var registry = MqttResourceTestContext.CreateRegistry();
        var resources = MqttResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var factory).ShouldBeTrue();

        var runtimeNode = factory(MqttResourceTestContext.CreateContext(
            MqttComponentTypes.Subscribe,
            new
            {
                connectionName = MqttResourceTestContext.ConnectionName,
                topicFilter = "devices/+",
                qualityOfService = "AtLeastOnce",
                receiveRetainedMessages = false,
                retainAsPublished = true,
                boundedCapacity = 4
            },
            resources));

        var output = runtimeNode.FindOutput(new PortName(MqttComponentPorts.Output));
        output.ShouldNotBeNull();
        output.ValueType.ShouldBe(typeof(MqttReceivedMessage));

        var messages = new BufferBlock<MqttReceivedMessage>();
        using var link = output.TryLinkTo(
            new InputPort<MqttReceivedMessage>(
                new PortAddress("test", new NodeName("messages"), new PortName("Input")),
                messages),
            propagateCompletion: true,
            out var error);
        error.ShouldBeNull();

        var node = runtimeNode.Node.ShouldBeOfType<Nodes.MqttSubscribeNode>();
        var errors = new BufferBlock<FluxFlow.Engine.Components.FlowError>();
        node.Errors.LinkTo(errors);

        await node.StartAsync();

        // Before any connect the loop reports not connected once and produces nothing.
        var flowError = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        flowError.Code.ShouldBe(MqttErrorCodes.SubscribeNotConnected);
        flowError.Context.ShouldNotBeNull();
        flowError.Context.ShouldContain("topicFilter=devices/+");
        flowError.Context.ShouldContain($"connectionName={MqttResourceTestContext.ConnectionName}");

        // No client => no messages, but Complete() still completes cleanly.
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        messages.TryReceive(out _).ShouldBeFalse();

        await node.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeNode_SubscribesAfterConnectAndFlowsMessages()
    {
        var adapter = new RecordingMqttClientAdapter();
        var factory = new RecordingMqttClientFactory(adapter, ownLease: false);
        var registry = MqttResourceTestContext.CreateRegistry(clientFactory: factory);
        var resources = MqttResourceTestContext.CreateResources(registry);
        var handle = MqttResourceTestContext.ResolveHandle(resources);
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var subFactory).ShouldBeTrue();

        var (node, messages) = StartSubscribe(subFactory, resources);

        // Before connect: nothing flows.
        await node.StartAsync();
        messages.TryReceive(out _).ShouldBeFalse();
        adapter.SubscribeCalls.ShouldBe(0);

        // After ConnectAsync (post-start): subscription opens.
        await handle.ConnectAsync();
        await adapter.Subscribed.WaitAsync(TimeSpan.FromSeconds(5));
        adapter.SubscribeCalls.ShouldBe(1);

        adapter.PushMessage(CreateMessage("devices/a", "m1"));
        var received = await messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        received.Topic.ShouldBe("devices/a");
        received.CorrelationId.ShouldBe("m1");

        // DisconnectAsync: the subscription is disposed.
        var subscription = adapter.Subscriptions[0];
        await handle.DisconnectAsync();
        await WaitForAsync(() => subscription.Disposed, TimeSpan.FromSeconds(5));

        await node.DisposeAsync();
        await ((IAsyncDisposable)handle).DisposeAsync();
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
        var registry = MqttResourceTestContext.CreateRegistry(clientFactory: factory);
        var resources = MqttResourceTestContext.CreateResources(registry);
        var handle = MqttResourceTestContext.ResolveHandle(resources);
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var subFactory).ShouldBeTrue();

        var (node, messages) = StartSubscribe(subFactory, resources);
        await node.StartAsync();

        // First lease (epoch 1).
        await handle.ConnectAsync();
        var firstEpoch = handle.ConnectionEpoch;
        await adapters[0].Subscribed.WaitAsync(TimeSpan.FromSeconds(5));
        adapters[0].SubscribeCalls.ShouldBe(1);
        adapters[0].PushMessage(CreateMessage("devices/a", "first"));
        (await messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).CorrelationId.ShouldBe("first");

        // Reconnect (Disconnect + Connect => new lease/epoch).
        await handle.DisconnectAsync();
        await handle.ConnectAsync();
        handle.ConnectionEpoch.ShouldBeGreaterThan(firstEpoch);
        await adapters[1].Subscribed.WaitAsync(TimeSpan.FromSeconds(5));
        adapters[1].SubscribeCalls.ShouldBe(1);

        adapters[1].PushMessage(CreateMessage("devices/b", "second"));
        (await messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).CorrelationId.ShouldBe("second");

        await node.DisposeAsync();
        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    [Fact]
    public async Task SubscribeNode_DoesNotDoubleSubscribeWithinSameLeaseEpoch()
    {
        var adapter = new RecordingMqttClientAdapter();
        var factory = new RecordingMqttClientFactory(adapter, ownLease: false);
        var registry = MqttResourceTestContext.CreateRegistry(clientFactory: factory);
        var resources = MqttResourceTestContext.CreateResources(registry);
        var handle = MqttResourceTestContext.ResolveHandle(resources);
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var subFactory).ShouldBeTrue();

        var (node, messages) = StartSubscribe(subFactory, resources);
        await node.StartAsync();

        await handle.ConnectAsync();
        await adapter.Subscribed.WaitAsync(TimeSpan.FromSeconds(5));
        adapter.SubscribeCalls.ShouldBe(1);

        // A within-lease Reconnecting -> Connected health blip on the SAME lease
        // (same epoch) must not resubscribe.
        adapter.PushHealth(new MqttClientHealthEvent { State = MqttClientHealthState.Reconnecting });
        adapter.PushHealth(new MqttClientHealthEvent { State = MqttClientHealthState.Connected });

        // Drive a message through to prove the original subscription is still pumping.
        adapter.PushMessage(CreateMessage("devices/a", "still-here"));
        (await messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).CorrelationId.ShouldBe("still-here");

        // The connection epoch never advanced and SubscribeAsync stayed at one.
        handle.ConnectionEpoch.ShouldBe(1);
        adapter.SubscribeCalls.ShouldBe(1);

        await node.DisposeAsync();
        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    private static (Nodes.MqttSubscribeNode Node, BufferBlock<MqttReceivedMessage> Messages) StartSubscribe(
        RuntimeNodeFactory factory,
        IReadOnlyDictionary<NodeName, RuntimeNode> resources,
        string topicFilter = "devices/+")
    {
        var runtimeNode = factory(MqttResourceTestContext.CreateContext(
            MqttComponentTypes.Subscribe,
            new { connectionName = MqttResourceTestContext.ConnectionName, topicFilter },
            resources));

        var messages = new BufferBlock<MqttReceivedMessage>();
        var output = runtimeNode.FindOutput(new PortName(MqttComponentPorts.Output));
        output.ShouldNotBeNull();
        // Link is intentionally kept alive for the lifetime of the test.
        output.TryLinkTo(
            new InputPort<MqttReceivedMessage>(
                new PortAddress("test", new NodeName("messages"), new PortName("Input")),
                messages),
            propagateCompletion: false,
            out var error);
        error.ShouldBeNull();

        return (runtimeNode.Node.ShouldBeOfType<Nodes.MqttSubscribeNode>(), messages);
    }

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

    [Fact]
    public async Task SubscribeNode_CompletesCleanlyOnComplete()
    {
        var registry = MqttResourceTestContext.CreateRegistry();
        var resources = MqttResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var factory).ShouldBeTrue();

        var runtimeNode = factory(MqttResourceTestContext.CreateContext(
            MqttComponentTypes.Subscribe,
            new { connectionName = MqttResourceTestContext.ConnectionName, topicFilter = "devices/+" },
            resources));

        await runtimeNode.Node.StartAsync();
        runtimeNode.Node.Completion.IsCompleted.ShouldBeFalse();

        runtimeNode.Node.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        runtimeNode.Node.Completion.IsCompletedSuccessfully.ShouldBeTrue();

        if (runtimeNode.Node is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
    }

    [Fact]
    public async Task SubscribeNode_DisposeCompletesNode()
    {
        var registry = MqttResourceTestContext.CreateRegistry();
        var resources = MqttResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var factory).ShouldBeTrue();

        var runtimeNode = factory(MqttResourceTestContext.CreateContext(
            MqttComponentTypes.Subscribe,
            new { connectionName = MqttResourceTestContext.ConnectionName, topicFilter = "devices/#" },
            resources));

        await runtimeNode.Node.StartAsync();
        await ((IAsyncDisposable)runtimeNode.Node).DisposeAsync();

        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        runtimeNode.Node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public void SubscribeNode_RejectsMissingConnectionName()
    {
        var registry = MqttResourceTestContext.CreateRegistry();
        var resources = MqttResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(MqttResourceTestContext.CreateContext(
                MqttComponentTypes.Subscribe,
                new { topicFilter = "devices/+" },
                resources)));

        exception.Message.ShouldContain("ConnectionName");
    }

    [Fact]
    public void SubscribeNode_FailsWhenConnectionResourceMissing()
    {
        var registry = MqttResourceTestContext.CreateRegistry();
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(MqttResourceTestContext.CreateContext(
                MqttComponentTypes.Subscribe,
                new { connectionName = "missing-broker", topicFilter = "devices/+" },
                new Dictionary<NodeName, RuntimeNode>())));

        exception.Message.ShouldContain("missing-broker");
    }

    [Fact]
    public void SubscribeNode_RequiresTopicFilter()
    {
        var registry = MqttResourceTestContext.CreateRegistry();
        var resources = MqttResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(MqttResourceTestContext.CreateContext(
                MqttComponentTypes.Subscribe,
                new { connectionName = MqttResourceTestContext.ConnectionName },
                resources)));

        exception.Message.ShouldContain("topic filter");
    }

    [Fact]
    public void SubscribeNode_RejectsInvalidTopicFilter()
    {
        var registry = MqttResourceTestContext.CreateRegistry();
        var resources = MqttResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(MqttResourceTestContext.CreateContext(
                MqttComponentTypes.Subscribe,
                new { connectionName = MqttResourceTestContext.ConnectionName, topicFilter = "devices/#/state" },
                resources)));

        exception.Message.ShouldContain("topicFilter");
    }
}
