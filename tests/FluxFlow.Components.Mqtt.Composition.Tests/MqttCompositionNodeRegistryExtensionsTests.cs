using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Composition.Tests;

public sealed class MqttCompositionNodeRegistryExtensionsTests
{
    [Fact]
    public void RegisterMqttNodes_registers_publish_and_trigger_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterMqttNodes();

        var publish = registry.Registrations[MqttCompositionNodeTypes.Publish];
        publish.Inputs[MqttCompositionPortNames.Input].MessageType.ShouldBe(
            typeof(MqttPublishRequest));
        publish.Outputs[MqttCompositionPortNames.Output].MessageType.ShouldBe(
            typeof(MqttPublishResult));

        var trigger = registry.Registrations[MqttCompositionNodeTypes.Trigger];
        trigger.Inputs[MqttCompositionPortNames.Responses].MessageType.ShouldBe(
            typeof(MqttTriggerResponse));
        trigger.Outputs[MqttCompositionPortNames.Output].MessageType.ShouldBe(
            typeof(MqttReceivedMessage));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_mqtt_metadata()
    {
        var metadata = DesignMetadataByType();

        metadata.Keys.ShouldBe([
            MqttCompositionNodeTypes.Publish,
            MqttCompositionNodeTypes.Trigger
        ], ignoreOrder: false);

        foreach (var item in metadata.Values)
        {
            ComponentDesignMetadataValidator.Validate(item).ShouldBeEmpty();
            item.Category.ShouldBe(new ComponentCategory("MQTT"));
            item.Options.ShouldNotContain(option =>
                option.Name.Value == MqttCompositionResourceNames.Publisher ||
                option.Name.Value == MqttCompositionResourceNames.TriggerSource ||
                option.Name.Value == MqttCompositionResourceNames.Clock);
        }
    }

    [Fact]
    public void Design_metadata_provider_describes_publish_ports_and_options()
    {
        var metadata = DesignMetadataByType()[MqttCompositionNodeTypes.Publish];
        var defaults = new MqttPublishOptions();

        metadata.DisplayName.ShouldBe("MQTT Publish");
        metadata.SuggestedEditorWidth.ShouldBe(420);
        AssertPorts<MqttPublishRequest, MqttPublishResult>(
            metadata,
            MqttCompositionPortNames.Input);

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "publishTimeoutMilliseconds",
            "boundedCapacity"
        ], ignoreOrder: false);
        AssertOption(
            metadata,
            "publishTimeoutMilliseconds",
            OptionValueKind.Number,
            defaults.PublishTimeoutMilliseconds,
            min: 1);
        AssertOption(
            metadata,
            "boundedCapacity",
            OptionValueKind.Number,
            defaults.BoundedCapacity,
            min: 1);
        AssertResources(
            metadata,
            (MqttCompositionResourceNames.Publisher, true, nameof(IMqttPublisher)),
            (MqttCompositionResourceNames.Clock, false, nameof(TimeProvider)));
    }

    [Fact]
    public void Design_metadata_provider_describes_trigger_ports_and_options()
    {
        var metadata = DesignMetadataByType()[MqttCompositionNodeTypes.Trigger];
        var defaults = new MqttTriggerOptions();

        metadata.DisplayName.ShouldBe("MQTT Trigger");
        metadata.SuggestedEditorWidth.ShouldBe(460);
        AssertPorts<MqttTriggerResponse, MqttReceivedMessage>(
            metadata,
            MqttCompositionPortNames.Responses);

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "topicFilter",
            "qualityOfService",
            "receiveRetainedMessages",
            "retainAsPublished",
            "boundedCapacity",
            "mode",
            "acknowledgement",
            "responseTimeout"
        ], ignoreOrder: false);

        var topicFilter = AssertOption(
            metadata,
            "topicFilter",
            OptionValueKind.Text,
            defaultValue: null);
        topicFilter.IsRequired.ShouldBeTrue();

        var qualityOfService = AssertOption(
            metadata,
            "qualityOfService",
            OptionValueKind.Enum,
            defaults.QualityOfService.ToString());
        qualityOfService.Choices.Select(choice => choice.Value).ShouldBe([
            MqttQualityOfService.AtMostOnce.ToString(),
            MqttQualityOfService.AtLeastOnce.ToString(),
            MqttQualityOfService.ExactlyOnce.ToString()
        ], ignoreOrder: false);

        AssertOption(
            metadata,
            "receiveRetainedMessages",
            OptionValueKind.Boolean,
            defaults.ReceiveRetainedMessages);
        AssertOption(
            metadata,
            "retainAsPublished",
            OptionValueKind.Boolean,
            defaults.RetainAsPublished);
        AssertOption(
            metadata,
            "boundedCapacity",
            OptionValueKind.Number,
            defaults.BoundedCapacity,
            min: 1);

        var mode = AssertOption(
            metadata,
            "mode",
            OptionValueKind.Enum,
            defaults.Mode.ToString());
        mode.Choices.Select(choice => choice.Value).ShouldBe([
            MqttTriggerMode.FireAndForget.ToString(),
            MqttTriggerMode.RequestReply.ToString()
        ], ignoreOrder: false);

        var acknowledgement = AssertOption(
            metadata,
            "acknowledgement",
            OptionValueKind.Enum,
            defaults.Acknowledgement.ToString());
        acknowledgement.Choices.Select(choice => choice.Value).ShouldBe([
            MqttTriggerAcknowledgement.None.ToString(),
            MqttTriggerAcknowledgement.OnEmit.ToString(),
            MqttTriggerAcknowledgement.OnSuccessfulResponse.ToString()
        ], ignoreOrder: false);

        AssertOption(
            metadata,
            "responseTimeout",
            OptionValueKind.Duration,
            defaults.ResponseTimeout,
            min: 0.000001);
        AssertResources(
            metadata,
            (MqttCompositionResourceNames.TriggerSource, true, nameof(IMqttTriggerSource)),
            (MqttCompositionResourceNames.Clock, false, nameof(TimeProvider)));
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var provider = new MqttComponentDesignMetadataProvider();
        var catalog = ComponentDesignMetadataCatalog.FromProviders([provider]);

        catalog.All.Count.ShouldBe(2);
        catalog.TryGet(
            new ComponentType(MqttCompositionNodeTypes.Publish),
            out var publishMetadata).ShouldBeTrue();
        catalog.TryGet(
            new ComponentType(MqttCompositionNodeTypes.Trigger),
            out var triggerMetadata).ShouldBeTrue();

        publishMetadata.ShouldNotBeNull().DisplayName.ShouldBe("MQTT Publish");
        triggerMetadata.ShouldNotBeNull().DisplayName.ShouldBe("MQTT Trigger");
    }

    [Fact]
    public async Task Hosted_publish_node_resolves_keyed_publisher_and_publishes()
    {
        var adapter = new RecordingMqttAdapter();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IMqttPublisher>("primary", adapter);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "publish",
                    MqttCompositionNodeTypes.Publish,
                    node => node
                        .Resource(MqttCompositionResourceNames.Publisher, "primary")
                        .Configure("publishTimeoutMilliseconds", 1_000)
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterMqttNodes())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var runtime = host.Runtime.ShouldNotBeNull();
        var publishNode = runtime.Nodes.ShouldHaveSingleItem();
        var input = publishNode.Descriptor.Inputs[MqttCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<MqttPublishRequest>>();
        var output = publishNode.Descriptor.Outputs[MqttCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<MqttPublishResult>>();
        var results = new BufferBlock<FlowMessage<MqttPublishResult>>();
        output.Source.LinkTo(
            results,
            new DataflowLinkOptions { PropagateCompletion = true });

        var request = new MqttPublishRequest
        {
            Topic = "devices/temperature",
            Payload = [1, 2, 3],
            QualityOfService = MqttQualityOfService.AtLeastOnce,
            Retain = true,
            Properties = new MqttPublishProperties { CorrelationId = "mqtt-correlation" }
        };

        (await input.Target.SendAsync(FlowMessage.Create(
                request,
                new CorrelationId("workflow-correlation")))
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        input.Target.Complete();

        var result = await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        adapter.Published.ShouldHaveSingleItem().ShouldBe(request);
        result.CorrelationId.ShouldBe(new CorrelationId("workflow-correlation"));
        result.Payload.Topic.ShouldBe("devices/temperature");
        result.Payload.PayloadBytes.ShouldBe(3);
        result.Payload.QualityOfService.ShouldBe(MqttQualityOfService.AtLeastOnce);
        result.Payload.Retain.ShouldBeTrue();
    }

    [Fact]
    public async Task Hosted_trigger_node_resolves_keyed_trigger_source_and_emits_messages()
    {
        var adapter = new RecordingMqttAdapter();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IMqttTriggerSource>("primary", adapter);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "trigger",
                    MqttCompositionNodeTypes.Trigger,
                    node => node
                        .Resource(MqttCompositionResourceNames.TriggerSource, "primary")
                        .Configure("topicFilter", "devices/+")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterMqttNodes())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var runtime = host.Runtime.ShouldNotBeNull();
        var triggerNode = runtime.Nodes.ShouldHaveSingleItem();
        var output = triggerNode.Descriptor.Outputs[MqttCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<MqttReceivedMessage>>();
        var messages = new BufferBlock<FlowMessage<MqttReceivedMessage>>();
        output.Source.LinkTo(
            messages,
            new DataflowLinkOptions { PropagateCompletion = true });

        await host.StartRuntimeAsync(CancellationToken.None);
        await adapter.Subscribed.WaitAsync(TimeSpan.FromSeconds(5));

        adapter.PushMessage(new MqttReceivedMessage
        {
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "devices/a",
            Payload = [9, 8, 7],
            CorrelationId = "mqtt-message"
        });

        var received = await messages.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await hostedService.StopAsync(CancellationToken.None);

        adapter.SubscribeCalls.ShouldBe(1);
        adapter.TriggerOptions.ShouldNotBeNull();
        adapter.TriggerOptions.TopicFilter.ShouldBe("devices/+");
        received.CorrelationId.ShouldBe(new CorrelationId("mqtt-message"));
        received.Payload.Topic.ShouldBe("devices/a");
        received.Payload.Payload.ShouldBe([9, 8, 7]);
    }

    [Fact]
    public async Task Missing_resource_reference_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "publish",
                    MqttCompositionNodeTypes.Publish))
                .Build())
            .RegisterNodes(registry => registry.RegisterMqttNodes())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(
                MqttCompositionResourceNames.Publisher,
                StringComparison.Ordinal));
    }

    private static IReadOnlyDictionary<string, ComponentDesignMetadata> DesignMetadataByType()
        => new MqttComponentDesignMetadataProvider()
            .GetMetadata()
            .ToDictionary(metadata => metadata.Type.Value, StringComparer.Ordinal);

    private static void AssertPorts<TInput, TOutput>(
        ComponentDesignMetadata metadata,
        string inputPortName)
    {
        metadata.Ports.Count.ShouldBe(2);

        var input = metadata.Ports[0];
        input.Name.ShouldBe(new ComponentPortName(inputPortName));
        input.Direction.ShouldBe(PortDirection.Input);
        input.ValueType.ShouldBe(typeof(TInput).Name);
        input.IsPrimary.ShouldBeTrue();
        input.Order.ShouldBe(0);

        var output = metadata.Ports[1];
        output.Name.ShouldBe(new ComponentPortName(MqttCompositionPortNames.Output));
        output.Direction.ShouldBe(PortDirection.Output);
        output.ValueType.ShouldBe(typeof(TOutput).Name);
        output.IsPrimary.ShouldBeTrue();
        output.Order.ShouldBe(1);
    }

    private static OptionDesignMetadata AssertOption(
        ComponentDesignMetadata metadata,
        string name,
        OptionValueKind kind,
        object? defaultValue,
        double? min = null)
    {
        var option = metadata.Options.Single(option => option.Name.Value == name);
        option.Kind.ShouldBe(kind);
        option.DefaultValue.ShouldBe(defaultValue);
        option.Min.ShouldBe(min);
        return option;
    }

    private static void AssertResources(
        ComponentDesignMetadata metadata,
        params (string Name, bool IsRequired, string ValueType)[] expected)
    {
        metadata.Resources.Count.ShouldBe(expected.Length);

        for (var index = 0; index < expected.Length; index++)
        {
            var resource = metadata.Resources[index];
            resource.Name.Value.ShouldBe(expected[index].Name);
            resource.Order.ShouldBe(index);
            resource.IsRequired.ShouldBe(expected[index].IsRequired);
            resource.ValueType.ShouldBe(expected[index].ValueType);
        }
    }

    private sealed class RecordingMqttAdapter :
        IMqttPublisher,
        IMqttTriggerSource
    {
        private readonly object _gate = new();
        private readonly Channel<IMqttReceivedContext> _incoming =
            Channel.CreateUnbounded<IMqttReceivedContext>();
        private readonly List<MqttPublishRequest> _published = [];
        private readonly TaskCompletionSource _subscribed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _subscribeCalls;

        public IReadOnlyList<MqttPublishRequest> Published
        {
            get
            {
                lock (_gate)
                {
                    return _published.ToArray();
                }
            }
        }

        public int SubscribeCalls => Volatile.Read(ref _subscribeCalls);

        public Task Subscribed => _subscribed.Task;

        public MqttTriggerOptions? TriggerOptions { get; private set; }

        public ValueTask PublishAsync(
            MqttPublishRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _published.Add(request);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<IMqttSubscription> SubscribeAsync(
            MqttTriggerOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _subscribeCalls);
            TriggerOptions = options;
            _subscribed.TrySetResult();
            return ValueTask.FromResult<IMqttSubscription>(
                new RecordingMqttSubscription(_incoming.Reader));
        }

        public void PushMessage(MqttReceivedMessage message)
            => _incoming.Writer.TryWrite(new RecordingMqttReceivedContext(message));
    }

    private sealed class RecordingMqttSubscription(
        ChannelReader<IMqttReceivedContext> reader)
        : IMqttSubscription
    {
        public IAsyncEnumerable<IMqttReceivedContext> Messages => reader.ReadAllAsync();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingMqttReceivedContext(MqttReceivedMessage message)
        : IMqttReceivedContext
    {
        public MqttReceivedMessage Message { get; } = message;

        public ValueTask AckAsync(CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask NackAsync(
            Exception? error = null,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}
