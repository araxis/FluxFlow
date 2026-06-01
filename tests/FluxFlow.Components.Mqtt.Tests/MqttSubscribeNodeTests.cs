using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttSubscribeNodeTests
{
    [Fact]
    public async Task SubscribeNode_EmitsAdapterMessages()
    {
        var message = new MqttReceivedMessage
        {
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "devices/temperature",
            Payload = [42],
            QualityOfService = MqttQualityOfService.AtLeastOnce
        };
        var adapter = new RecordingMqttClientAdapter(message);
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMqttComponents(options => options.UseClientFactory(new RecordingMqttClientFactory(adapter)));
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var factory).ShouldBeTrue();

        var runtimeNode = factory(CreateContext(
            MqttComponentTypes.Subscribe,
            new
            {
                connectionName = "main-broker",
                topicFilter = "devices/+",
                qualityOfService = "AtLeastOnce",
                receiveRetainedMessages = false,
                retainAsPublished = true,
                boundedCapacity = 4
            }));
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
        link.ShouldNotBeNull();

        await runtimeNode.Node.StartAsync();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var received = await messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        received.Topic.ShouldBe("devices/temperature");
        received.Payload.ShouldBe([42]);
        adapter.SubscriptionOptions.ShouldNotBeNull();
        adapter.SubscriptionOptions.TopicFilter.ShouldBe("devices/+");
        adapter.SubscriptionOptions.ReceiveRetainedMessages.ShouldBeFalse();
        adapter.SubscriptionOptions.RetainAsPublished.ShouldBeTrue();

        if (runtimeNode.Node is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }

        adapter.Disposed.ShouldBeTrue();
        adapter.Subscriptions[0].Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task SubscribeNode_StartsInBackgroundUntilCompleted()
    {
        var adapter = new RecordingMqttClientAdapter(waitForCancellation: true);
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMqttComponents(options => options.UseClientFactory(new RecordingMqttClientFactory(adapter)));
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var factory).ShouldBeTrue();

        var runtimeNode = factory(CreateContext(MqttComponentTypes.Subscribe, new { topicFilter = "devices/+" }));

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));

        runtimeNode.Node.Completion.IsCompleted.ShouldBeFalse();

        runtimeNode.Node.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        if (runtimeNode.Node is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }

        adapter.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task SubscribeNode_DoesNotDisposeSharedAdapter()
    {
        var adapter = new RecordingMqttClientAdapter(waitForCancellation: true);
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMqttComponents(options => options.UseClientFactory(new RecordingMqttClientFactory(
                adapter,
                disposeAdapter: false)));
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var factory).ShouldBeTrue();

        var runtimeNode = factory(CreateContext(MqttComponentTypes.Subscribe, new { topicFilter = "devices/+" }));

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        runtimeNode.Node.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        if (runtimeNode.Node is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }

        adapter.Disposed.ShouldBeFalse();
        adapter.Subscriptions[0].Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task SubscribeNode_SharedAdapterCreatesIndependentSubscriptions()
    {
        var message = new MqttReceivedMessage
        {
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "devices/temperature",
            Payload = [7]
        };
        var adapter = new RecordingMqttClientAdapter(message);
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMqttComponents(options => options.UseClientFactory(new RecordingMqttClientFactory(
                adapter,
                disposeAdapter: false)));
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var factory).ShouldBeTrue();

        var firstNode = factory(CreateContext(MqttComponentTypes.Subscribe, new { topicFilter = "devices/+" }));
        var secondNode = factory(CreateContext(MqttComponentTypes.Subscribe, new { topicFilter = "devices/#" }));
        var firstMessages = new BufferBlock<MqttReceivedMessage>();
        var secondMessages = new BufferBlock<MqttReceivedMessage>();
        firstNode.FindOutput(new PortName(MqttComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<MqttReceivedMessage>(
                    new PortAddress("test", new NodeName("first"), new PortName("Input")),
                    firstMessages),
                propagateCompletion: true,
                out _);
        secondNode.FindOutput(new PortName(MqttComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<MqttReceivedMessage>(
                    new PortAddress("test", new NodeName("second"), new PortName("Input")),
                    secondMessages),
                propagateCompletion: true,
                out _);

        await firstNode.Node.StartAsync();
        await secondNode.Node.StartAsync();
        await firstNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await secondNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var first = await firstMessages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await secondMessages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        first.Payload.ShouldBe([7]);
        second.Payload.ShouldBe([7]);
        adapter.Subscriptions.Count.ShouldBe(2);

        if (firstNode.Node is IAsyncDisposable firstDisposable)
        {
            await firstDisposable.DisposeAsync();
        }

        if (secondNode.Node is IAsyncDisposable secondDisposable)
        {
            await secondDisposable.DisposeAsync();
        }

        adapter.Disposed.ShouldBeFalse();
    }

    [Fact]
    public async Task SubscribeNode_FailsStartupWhenSubscriptionCannotStart()
    {
        var adapter = new RecordingMqttClientAdapter(
            waitForCancellation: false,
            subscribeStartupException: new InvalidOperationException("subscribe rejected"),
            subscriptionException: null);
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMqttComponents(options => options.UseClientFactory(new RecordingMqttClientFactory(adapter)));
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var factory).ShouldBeTrue();

        var runtimeNode = factory(CreateContext(MqttComponentTypes.Subscribe, new { topicFilter = "devices/+" }));
        var errors = new BufferBlock<FluxFlow.Engine.Components.FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => runtimeNode.Node.StartAsync());

        exception.Message.ShouldContain("failed to start");
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MqttErrorCodes.SubscribeStartupFailed);
        error.Context.ShouldNotBeNull();
        error.Context.ShouldContain("topicFilter=devices/+");
        adapter.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task SubscribeNode_FaultsWhenAdapterStreamFails()
    {
        var adapter = new RecordingMqttClientAdapter(
            new InvalidOperationException("connection lost"));
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMqttComponents(options => options.UseClientFactory(new RecordingMqttClientFactory(adapter)));
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var factory).ShouldBeTrue();

        var runtimeNode = factory(CreateContext(MqttComponentTypes.Subscribe, new { topicFilter = "devices/+" }));
        var errors = new BufferBlock<FluxFlow.Engine.Components.FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);

        await runtimeNode.Node.StartAsync();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        exception.Message.ShouldBe("connection lost");

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MqttErrorCodes.SubscribeFailed);

        if (runtimeNode.Node is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
    }

    [Fact]
    public void SubscribeNode_RequiresTopicFilter()
    {
        var adapter = new RecordingMqttClientAdapter();
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMqttComponents(options => options.UseClientFactory(new RecordingMqttClientFactory(adapter)));
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(CreateContext(MqttComponentTypes.Subscribe, new { })));

        exception.Message.ShouldContain("topic filter");
    }

    [Fact]
    public void SubscribeNode_RejectsInvalidTopicFilter()
    {
        var adapter = new RecordingMqttClientAdapter();
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMqttComponents(options => options.UseClientFactory(new RecordingMqttClientFactory(adapter)));
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(CreateContext(MqttComponentTypes.Subscribe, new { topicFilter = "devices/#/state" })));

        exception.Message.ShouldContain("topicFilter");
    }

    [Fact]
    public async Task SubscribeNode_EmitsDiagnostics()
    {
        var adapter = new RecordingMqttClientAdapter(new MqttReceivedMessage
        {
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "devices/temperature",
            Payload = [1]
        });
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMqttComponents(options => options.UseClientFactory(new RecordingMqttClientFactory(adapter)));
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out var factory).ShouldBeTrue();

        var runtimeNode = factory(CreateContext(MqttComponentTypes.Subscribe, new { topicFilter = "devices/+" }));
        var diagnostics = new BufferBlock<FluxFlow.Engine.Components.FlowDiagnostic>();
        var events = new BufferBlock<FluxFlow.Engine.Components.FlowEvent>();
        runtimeNode.Node.ShouldBeOfType<Nodes.MqttSubscribeNode>()
            .Diagnostics.LinkTo(
                diagnostics,
                new DataflowLinkOptions { PropagateCompletion = true });
        runtimeNode.Node.ShouldBeAssignableTo<FluxFlow.Engine.Components.IFlowEventSource>()!
            .Events.LinkTo(
                events,
                new DataflowLinkOptions { PropagateCompletion = true });

        await runtimeNode.Node.StartAsync();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var names = new List<string>();
        while (names.Count < 3)
        {
            var diagnostic = await diagnostics.ReceiveAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            names.Add(diagnostic.Name);
        }

        names.ShouldContain(MqttDiagnosticNames.SubscribeStarted);
        names.ShouldContain(MqttDiagnosticNames.SubscribeReceived);
        names.ShouldContain(MqttDiagnosticNames.SubscribeStopped);
        var flowEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        flowEvent.Type.ShouldBe(MqttEventNames.SubscribeReceived);
        flowEvent.GetAttribute("retain").ShouldBe("False");
    }

    private static RuntimeNodeFactoryContext CreateContext(NodeType type, object configuration)
        => new(
            new NodeName("node"),
            new NodeDefinition
            {
                Type = type,
                Configuration = ToConfiguration(configuration)
            },
            "main",
            new Dictionary<NodeName, RuntimeNode>());

    private static Dictionary<string, JsonElement> ToConfiguration(object configuration)
    {
        var root = JsonSerializer.SerializeToElement(configuration);
        return root.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }
}
