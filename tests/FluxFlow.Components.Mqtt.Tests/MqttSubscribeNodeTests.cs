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
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 2, 3, 10, 1, 2, TimeSpan.Zero));
        var registry = MqttResourceTestContext.CreateRegistry(clock);
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
        var diagnostics = new BufferBlock<FluxFlow.Engine.Components.FlowDiagnostic>();
        node.Errors.LinkTo(errors);
        node.Diagnostics.LinkTo(diagnostics);

        await node.StartAsync();

        var flowError = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        flowError.Code.ShouldBe(MqttErrorCodes.SubscribeNotConnected);
        flowError.Message.ShouldContain("does not establish a client");
        flowError.Context.ShouldNotBeNull();
        flowError.Context.ShouldContain("topicFilter=devices/+");
        flowError.Context.ShouldContain($"connectionName={MqttResourceTestContext.ConnectionName}");

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(MqttDiagnosticNames.SubscribeFailed);
        diagnostic.Level.ShouldBe(FluxFlow.Engine.Components.FlowDiagnosticLevel.Error);
        diagnostic.Attributes["connectionName"].ShouldBe(MqttResourceTestContext.ConnectionName);

        // No client => no messages, but Complete() still completes cleanly.
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        messages.TryReceive(out _).ShouldBeFalse();

        await node.DisposeAsync();
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
