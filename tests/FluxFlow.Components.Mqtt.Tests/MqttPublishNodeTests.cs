using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttPublishNodeTests
{
    [Fact]
    public async Task PublishNode_ReportsNotConnectedForValidRequest()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 2, 3, 7, 1, 2, TimeSpan.Zero));
        var registry = MqttResourceTestContext.CreateRegistry(clock);
        var resources = MqttResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(MqttComponentTypes.Publish, out var factory).ShouldBeTrue();

        var runtimeNode = factory(MqttResourceTestContext.CreateContext(
            MqttComponentTypes.Publish,
            new
            {
                connectionName = MqttResourceTestContext.ConnectionName,
                defaultTopic = "devices/temperature",
                retain = true,
                qualityOfService = "AtLeastOnce",
                boundedCapacity = 4
            },
            resources));

        var input = runtimeNode.FindInput(new PortName(MqttComponentPorts.Input))
            .ShouldBeOfType<InputPort<MqttPublishRequest>>();
        var results = new BufferBlock<MqttPublishResult>();
        using var link = runtimeNode.FindOutput(new PortName(MqttComponentPorts.Result))!
            .TryLinkTo(
                new InputPort<MqttPublishResult>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    results),
                propagateCompletion: true,
                out var linkError);
        linkError.ShouldBeNull();

        var errors = new BufferBlock<FluxFlow.Engine.Components.FlowError>();
        var events = new BufferBlock<FluxFlow.Engine.Components.FlowEvent>();
        var diagnostics = new BufferBlock<FluxFlow.Engine.Components.FlowDiagnostic>();
        var node = runtimeNode.Node.ShouldBeOfType<Nodes.MqttPublishNode>();
        node.Errors.LinkTo(errors);
        node.Events.LinkTo(events);
        node.Diagnostics.LinkTo(diagnostics);

        await node.StartAsync();
        await input.Target.SendAsync(new MqttPublishRequest
        {
            Payload = [1, 2, 3],
            PayloadPreview = "010203",
            CorrelationId = "abc"
        });
        input.Target.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // No client => no result is produced; the node reports not connected.
        results.TryReceive(out _).ShouldBeFalse();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MqttErrorCodes.PublishNotConnected);
        error.Message.ShouldContain("does not establish a client");
        error.Context.ShouldNotBeNull();
        error.Context.ShouldContain("correlationId=abc");
        error.Context.ShouldContain($"connectionName={MqttResourceTestContext.ConnectionName}");

        var flowEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        flowEvent.Type.ShouldBe(MqttEventNames.PublishFailed);
        flowEvent.Status.ShouldBe("failed");
        flowEvent.Subject.ShouldBe("devices/temperature");
        flowEvent.Timestamp.ShouldBe(clock.GetUtcNow());
        flowEvent.GetAttribute("correlationId").ShouldBe("abc");

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(MqttDiagnosticNames.PublishFailed);
        diagnostic.Level.ShouldBe(FluxFlow.Engine.Components.FlowDiagnosticLevel.Error);
        diagnostic.Attributes["connectionName"].ShouldBe(MqttResourceTestContext.ConnectionName);

        await node.DisposeAsync();
    }

    [Fact]
    public async Task PublishNode_ReportsErrorWhenTopicIsMissing()
    {
        var registry = MqttResourceTestContext.CreateRegistry();
        var resources = MqttResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(MqttComponentTypes.Publish, out var factory).ShouldBeTrue();

        var runtimeNode = factory(MqttResourceTestContext.CreateContext(
            MqttComponentTypes.Publish,
            new { connectionName = MqttResourceTestContext.ConnectionName },
            resources));
        var input = runtimeNode.FindInput(new PortName(MqttComponentPorts.Input))
            .ShouldBeOfType<InputPort<MqttPublishRequest>>();
        var errors = new BufferBlock<FluxFlow.Engine.Components.FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(new MqttPublishRequest { Payload = [1] });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MqttErrorCodes.PublishInvalidTopic);
        error.Message.ShouldContain("topic");
    }

    [Fact]
    public async Task PublishNode_ReportsErrorWhenTopicContainsWildcard()
    {
        var registry = MqttResourceTestContext.CreateRegistry();
        var resources = MqttResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(MqttComponentTypes.Publish, out var factory).ShouldBeTrue();

        var runtimeNode = factory(MqttResourceTestContext.CreateContext(
            MqttComponentTypes.Publish,
            new { connectionName = MqttResourceTestContext.ConnectionName },
            resources));
        var input = runtimeNode.FindInput(new PortName(MqttComponentPorts.Input))
            .ShouldBeOfType<InputPort<MqttPublishRequest>>();
        var errors = new BufferBlock<FluxFlow.Engine.Components.FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(new MqttPublishRequest
        {
            Topic = "devices/+",
            Payload = [1]
        });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MqttErrorCodes.PublishInvalidTopic);
        error.Message.ShouldContain("wildcard");
    }

    [Fact]
    public void PublishNode_RejectsMissingConnectionName()
    {
        var registry = MqttResourceTestContext.CreateRegistry();
        var resources = MqttResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(MqttComponentTypes.Publish, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(MqttResourceTestContext.CreateContext(
                MqttComponentTypes.Publish,
                new { defaultTopic = "devices/state" },
                resources)));

        exception.Message.ShouldContain("ConnectionName");
    }

    [Fact]
    public void PublishNode_FailsWhenConnectionResourceMissing()
    {
        var registry = MqttResourceTestContext.CreateRegistry();
        registry.TryGetFactory(MqttComponentTypes.Publish, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(MqttResourceTestContext.CreateContext(
                MqttComponentTypes.Publish,
                new { connectionName = "missing-broker", defaultTopic = "devices/state" },
                new Dictionary<NodeName, RuntimeNode>())));

        exception.Message.ShouldContain("missing-broker");
    }

    [Fact]
    public void PublishNode_RejectsInvalidDefaultTopic()
    {
        var registry = MqttResourceTestContext.CreateRegistry();
        var resources = MqttResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(MqttComponentTypes.Publish, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(MqttResourceTestContext.CreateContext(
                MqttComponentTypes.Publish,
                new { connectionName = MqttResourceTestContext.ConnectionName, defaultTopic = "devices/#" },
                resources)));

        exception.Message.ShouldContain("defaultTopic");
    }

    [Fact]
    public void PublishNode_RejectsInvalidPublishTimeout()
    {
        var registry = MqttResourceTestContext.CreateRegistry();
        var resources = MqttResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(MqttComponentTypes.Publish, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(MqttResourceTestContext.CreateContext(
                MqttComponentTypes.Publish,
                new { connectionName = MqttResourceTestContext.ConnectionName, publishTimeoutMilliseconds = 0 },
                resources)));

        exception.Message.ShouldContain("PublishTimeoutMilliseconds");
    }

    [Fact]
    public async Task PublishNode_ReportsErrorWhenPayloadIsMissing()
    {
        var registry = MqttResourceTestContext.CreateRegistry();
        var resources = MqttResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(MqttComponentTypes.Publish, out var factory).ShouldBeTrue();

        var runtimeNode = factory(MqttResourceTestContext.CreateContext(
            MqttComponentTypes.Publish,
            new { connectionName = MqttResourceTestContext.ConnectionName, defaultTopic = "devices/state" },
            resources));
        var input = runtimeNode.FindInput(new PortName(MqttComponentPorts.Input))
            .ShouldBeOfType<InputPort<MqttPublishRequest>>();
        var errors = new BufferBlock<FluxFlow.Engine.Components.FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(new MqttPublishRequest { Payload = null! });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MqttErrorCodes.PublishInvalidPayload);
        error.Message.ShouldContain("payload");
    }
}
