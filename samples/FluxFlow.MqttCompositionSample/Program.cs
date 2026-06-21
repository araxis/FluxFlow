using System.Runtime.CompilerServices;
using System.Text;
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var seedMessages = new[]
{
    CreateReceived("devices/pump-01/state", "online", "pump-01-state"),
    CreateReceived("devices/pump-02/state", "offline", "pump-02-state")
};

var configurationPublished = await RunConfigurationCompositionAsync(seedMessages);
PrintPublished("configuration", configurationPublished);

var fluentPublished = await RunFluentCompositionAsync(seedMessages);
PrintPublished("fluent", fluentPublished);

return 0;

static async Task<IReadOnlyList<MqttPublishRequest>> RunConfigurationCompositionAsync(
    IReadOnlyList<MqttReceivedMessage> messages)
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .Build();

    return await RunHostedCompositionAsync(
        messages,
        services => services.AddFluxFlowComposition(configuration));
}

static async Task<IReadOnlyList<MqttPublishRequest>> RunFluentCompositionAsync(
    IReadOnlyList<MqttReceivedMessage> messages)
{
    var definition = CompositionDefinitionBuilder
        .Create()
        .Workflow("main", workflow => workflow
            .Node("inbound", MqttCompositionNodeTypes.Trigger, node => node
                .Resource(MqttCompositionResourceNames.TriggerSource, "memory")
                .Configure("topicFilter", "devices/+/state")
                .Configure("boundedCapacity", 16))
            .Node("reply", SampleNodeTypes.MqttReply, node => node
                .Configure("replyTopicSuffix", "/reply")
                .Configure("payloadPrefix", "ACK: "))
            .Node("outbound", MqttCompositionNodeTypes.Publish, node => node
                .Resource(MqttCompositionResourceNames.Publisher, "memory")
                .Configure("publishTimeoutMilliseconds", 1_000)
                .Configure("boundedCapacity", 16))
            .Link("inbound.Output", "reply.Input")
            .Link("reply.Output", "outbound.Input"))
        .Build();

    return await RunHostedCompositionAsync(
        messages,
        services => services.AddFluxFlowComposition(definition));
}

static async Task<IReadOnlyList<MqttPublishRequest>> RunHostedCompositionAsync(
    IReadOnlyList<MqttReceivedMessage> messages,
    Func<IServiceCollection, CompositionHostingBuilder> addComposition)
{
    var adapter = new InMemoryMqttAdapter(messages);
    var services = new ServiceCollection();

    services.AddKeyedSingleton<IMqttPublisher>("memory", adapter);
    services.AddKeyedSingleton<IMqttTriggerSource>("memory", adapter);

    addComposition(services)
        .RegisterNodes(RegisterSampleNodes);

    await using var provider = services.BuildServiceProvider();
    var hostedService = provider.GetServices<IHostedService>().Single();

    await hostedService.StartAsync(CancellationToken.None);

    var host = provider.GetRequiredService<ICompositionRuntimeHost>();
    await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    await hostedService.StopAsync(CancellationToken.None);

    if (host.Diagnostics.Count > 0)
    {
        throw new InvalidOperationException(string.Join(
            Environment.NewLine,
            host.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    return adapter.Published;
}

static void RegisterSampleNodes(CompositionNodeRegistry registry)
{
    registry
        .RegisterMqttNodes()
        .Register(
            SampleNodeTypes.MqttReply,
            context =>
            {
                var options = context.BindConfiguration<ReplyOptions>();
                var node = new MqttReplyMapperNode(options);
                return ValueTask.FromResult(ComposedNode.Create(
                    node,
                    inputs:
                    [
                        CompositionPorts.Input<MqttReceivedMessage>(
                            "Input",
                            node.Input)
                    ],
                    outputs:
                    [
                        CompositionPorts.Output<MqttPublishRequest>(
                            "Output",
                            node.Output)
                    ],
                    events: node.Events,
                    errors: node.Errors));
            },
            inputs: [CompositionPorts.Metadata<MqttReceivedMessage>("Input")],
            outputs: [CompositionPorts.Metadata<MqttPublishRequest>("Output")]);
}

static MqttReceivedMessage CreateReceived(
    string topic,
    string payload,
    string correlationId)
{
    var payloadBytes = Encoding.UTF8.GetBytes(payload);
    return new MqttReceivedMessage
    {
        Timestamp = DateTimeOffset.UtcNow,
        Topic = topic,
        Payload = payloadBytes,
        PayloadPreview = payload,
        ContentType = "text/plain",
        CorrelationId = correlationId
    };
}

static void PrintPublished(
    string label,
    IReadOnlyList<MqttPublishRequest> published)
{
    Console.WriteLine($"{label}:");
    foreach (var request in published)
    {
        Console.WriteLine(
            $"  {request.Topic} -> {Encoding.UTF8.GetString(request.Payload)}");
    }
}

internal static class SampleNodeTypes
{
    public const string MqttReply = "sample.mqtt.reply";
}

internal sealed record ReplyOptions
{
    public string ReplyTopicSuffix { get; init; } = "/reply";

    public string PayloadPrefix { get; init; } = "ACK: ";
}

internal sealed class MqttReplyMapperNode(ReplyOptions options)
    : FlowNode<MqttReceivedMessage, MqttPublishRequest>
{
    protected override Task ProcessAsync(FlowMessage<MqttReceivedMessage> message)
    {
        var received = message.Payload;
        var receivedPayload = Encoding.UTF8.GetString(received.Payload);
        var publishPayload = $"{options.PayloadPrefix}{receivedPayload}";
        var publishPayloadBytes = Encoding.UTF8.GetBytes(publishPayload);

        Emit(message.With(new MqttPublishRequest
        {
            Topic = $"{received.Topic}{options.ReplyTopicSuffix}",
            Payload = publishPayloadBytes,
            PayloadPreview = publishPayload,
            ContentType = "text/plain",
            QualityOfService = received.QualityOfService,
            Properties = new MqttPublishProperties
            {
                CorrelationId = received.CorrelationId
            }
        }));

        return Task.CompletedTask;
    }
}

internal sealed class InMemoryMqttAdapter(IReadOnlyList<MqttReceivedMessage> messages) :
    IMqttPublisher,
    IMqttTriggerSource
{
    private readonly object _gate = new();
    private readonly List<MqttPublishRequest> _published = [];

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
        return ValueTask.FromResult<IMqttSubscription>(
            new InMemoryMqttSubscription(messages));
    }
}

internal sealed class InMemoryMqttSubscription(IReadOnlyList<MqttReceivedMessage> messages)
    : IMqttSubscription
{
    public IAsyncEnumerable<IMqttReceivedContext> Messages => ReadAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async IAsyncEnumerable<IMqttReceivedContext> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new InMemoryMqttReceivedContext(message);
        }
    }
}

internal sealed class InMemoryMqttReceivedContext(MqttReceivedMessage message)
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
