using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttPublishNodeTests
{
    [Fact]
    public async Task PublishNode_PublishesRequestWithStaticDefaults()
    {
        var adapter = new RecordingMqttClientAdapter();
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMqttComponents(options => options.UseClientFactory(new RecordingMqttClientFactory(adapter)));
        registry.TryGetFactory(MqttComponentTypes.Publish, out var factory).ShouldBeTrue();

        var runtimeNode = factory(CreateContext(
            MqttComponentTypes.Publish,
            new
            {
                defaultTopic = "devices/temperature",
                retain = true,
                qualityOfService = "AtLeastOnce",
                boundedCapacity = 4
            }));

        var input = runtimeNode.FindInput(new PortName(MqttComponentPorts.Input))
            .ShouldBeOfType<InputPort<MqttPublishRequest>>();
        var output = runtimeNode.FindOutput(new PortName(MqttComponentPorts.Result));
        output.ShouldNotBeNull();
        output.ValueType.ShouldBe(typeof(MqttPublishResult));

        var results = new BufferBlock<MqttPublishResult>();
        using var link = output.TryLinkTo(
            new InputPort<MqttPublishResult>(
                new PortAddress("test", new NodeName("results"), new PortName("Input")),
                results),
            propagateCompletion: true,
            out var error);
        error.ShouldBeNull();
        link.ShouldNotBeNull();

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(new MqttPublishRequest
        {
            Payload = [1, 2, 3],
            CorrelationId = "abc"
        });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        adapter.Published.Count.ShouldBe(1);
        adapter.Published[0].Topic.ShouldBe("devices/temperature");
        adapter.Published[0].Retain.ShouldBe(true);
        adapter.Published[0].QualityOfService.ShouldBe(MqttQualityOfService.AtLeastOnce);

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Topic.ShouldBe("devices/temperature");
        result.PayloadBytes.ShouldBe(3);
        result.CorrelationId.ShouldBe("abc");

        if (runtimeNode.Node is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }

        adapter.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task PublishNode_ReportsErrorWhenTopicIsMissing()
    {
        var adapter = new RecordingMqttClientAdapter();
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMqttComponents(options => options.UseClientFactory(new RecordingMqttClientFactory(adapter)));
        registry.TryGetFactory(MqttComponentTypes.Publish, out var factory).ShouldBeTrue();

        var runtimeNode = factory(CreateContext(MqttComponentTypes.Publish, new { }));
        var input = runtimeNode.FindInput(new PortName(MqttComponentPorts.Input))
            .ShouldBeOfType<InputPort<MqttPublishRequest>>();
        var errors = new BufferBlock<FluxFlow.Engine.Components.FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(new MqttPublishRequest { Payload = [1] });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        adapter.Published.ShouldBeEmpty();
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MqttErrorCodes.PublishFailed);
        error.Message.ShouldContain("topic");
    }

    [Fact]
    public async Task PublishNode_ReportsErrorWhenPayloadIsMissing()
    {
        var adapter = new RecordingMqttClientAdapter();
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMqttComponents(options => options.UseClientFactory(new RecordingMqttClientFactory(adapter)));
        registry.TryGetFactory(MqttComponentTypes.Publish, out var factory).ShouldBeTrue();

        var runtimeNode = factory(CreateContext(MqttComponentTypes.Publish, new { defaultTopic = "devices/state" }));
        var input = runtimeNode.FindInput(new PortName(MqttComponentPorts.Input))
            .ShouldBeOfType<InputPort<MqttPublishRequest>>();
        var errors = new BufferBlock<FluxFlow.Engine.Components.FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(new MqttPublishRequest { Payload = null! });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        adapter.Published.ShouldBeEmpty();
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MqttErrorCodes.PublishFailed);
        error.Message.ShouldContain("payload");
    }

    [Fact]
    public async Task PublishNode_EmitsDiagnosticsAndEvents()
    {
        var adapter = new RecordingMqttClientAdapter();
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMqttComponents(options => options.UseClientFactory(new RecordingMqttClientFactory(adapter)));
        registry.TryGetFactory(MqttComponentTypes.Publish, out var factory).ShouldBeTrue();

        var runtimeNode = factory(CreateContext(MqttComponentTypes.Publish, new { defaultTopic = "devices/state" }));
        var node = runtimeNode.Node.ShouldBeOfType<Nodes.MqttPublishNode>();
        var input = runtimeNode.FindInput(new PortName(MqttComponentPorts.Input))
            .ShouldBeOfType<InputPort<MqttPublishRequest>>();
        var diagnostics = new BufferBlock<FluxFlow.Engine.Components.FlowDiagnostic>();
        var events = new BufferBlock<FluxFlow.Engine.Components.FlowEvent>();
        node.Diagnostics.LinkTo(diagnostics);
        node.Events.LinkTo(events);

        await node.StartAsync();
        await input.Target.SendAsync(new MqttPublishRequest { Payload = [9] });
        input.Target.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(MqttDiagnosticNames.PublishSucceeded);
        var flowEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        flowEvent.Type.ShouldBe(MqttEventNames.PublishSucceeded);
        flowEvent.Channel.ShouldBe(MqttEventNames.PublishSucceeded);
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
