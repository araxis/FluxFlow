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
                topicFilter = "devices/+",
                qualityOfService = "AtLeastOnce",
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

        if (runtimeNode.Node is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }

        adapter.Disposed.ShouldBeTrue();
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
        runtimeNode.Node.ShouldBeOfType<Nodes.MqttSubscribeNode>()
            .Diagnostics.LinkTo(diagnostics);

        await runtimeNode.Node.StartAsync();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var names = new List<string>();
        while (diagnostics.TryReceive(out var diagnostic))
        {
            names.Add(diagnostic.Name);
        }

        names.ShouldContain(MqttDiagnosticNames.SubscribeStarted);
        names.ShouldContain(MqttDiagnosticNames.SubscribeReceived);
        names.ShouldContain(MqttDiagnosticNames.SubscribeStopped);
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
