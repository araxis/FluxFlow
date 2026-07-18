using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var messages = new[]
{
    new SampleMessage("devices/pump-01/state/reply", "ACK: online"),
    new SampleMessage("devices/pump-02/state/reply", "ACK: offline")
};

var configurationPublished = await RunConfigurationCompositionAsync(messages);
PrintPublished("configuration", configurationPublished);

var fluentPublished = await RunFluentCompositionAsync(messages);
PrintPublished("fluent", fluentPublished);

return 0;

static async Task<IReadOnlyList<MqttPublishMessage>> RunConfigurationCompositionAsync(
    IReadOnlyList<SampleMessage> messages)
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .Build();

    return await RunHostedCompositionAsync(
        messages,
        services => services.AddFluxFlowComposition(configuration));
}

static async Task<IReadOnlyList<MqttPublishMessage>> RunFluentCompositionAsync(
    IReadOnlyList<SampleMessage> messages)
{
    var definition = CompositionDefinitionBuilder
        .Create()
        .Workflow("main", workflow => workflow
            .Node("source", SampleNodeTypes.PublishSource)
            .Node("outbound", MqttCompositionNodeTypes.Publish, node => node
                .Resource(MqttCompositionResourceNames.Client, "memory")
                .Configure("maximumPendingRequests", 16))
            .Link("source.Output", "outbound.Input"))
        .Build();

    return await RunHostedCompositionAsync(
        messages,
        services => services.AddFluxFlowComposition(definition));
}

static async Task<IReadOnlyList<MqttPublishMessage>> RunHostedCompositionAsync(
    IReadOnlyList<SampleMessage> messages,
    Func<IServiceCollection, CompositionHostingBuilder> addComposition)
{
    var controller = new RecordingMqttController();
    var services = new ServiceCollection();
    services.AddSingleton(new SampleMessages(messages));
    services.AddKeyedSingleton<IMqttClientController>("memory", controller);

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

    return controller.Published;
}

static void RegisterSampleNodes(CompositionNodeRegistry registry)
{
    registry
        .RegisterMqttNodes()
        .Register(
            SampleNodeTypes.PublishSource,
            context =>
            {
                var messages = context.Services.GetRequiredService<SampleMessages>();
                var node = new MqttPublishSourceNode(messages.Values);
                return ValueTask.FromResult(ComposedNode.Create(
                    node,
                    outputs:
                    [
                        CompositionPorts.Output<MqttPublishMessage>(
                            MqttCompositionPortNames.Output,
                            node.Output)
                    ],
                    events: node.Events,
                    errors: node.Errors));
            },
            outputs:
            [
                CompositionPorts.Metadata<MqttPublishMessage>(
                    MqttCompositionPortNames.Output)
            ]);
}

static void PrintPublished(
    string label,
    IReadOnlyList<MqttPublishMessage> published)
{
    Console.WriteLine($"{label}:");
    foreach (var message in published)
    {
        Console.WriteLine(
            $"  {message.Topic} -> " +
            System.Text.Encoding.UTF8.GetString(message.Content.OriginalBytes.AsSpan()));
    }
}

internal static class SampleNodeTypes
{
    public const string PublishSource = "sample.mqtt.publish-source";
}

internal sealed record SampleMessage(string Topic, string Content);

internal sealed record SampleMessages(IReadOnlyList<SampleMessage> Values);

internal sealed class MqttPublishSourceNode(IReadOnlyList<SampleMessage> messages)
    : FlowSource<MqttPublishMessage>
{
    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Emit(FlowMessage.Create(new MqttPublishMessage
            {
                Topic = message.Topic,
                Content = FlowContent.FromBytes(
                    System.Text.Encoding.UTF8.GetBytes(message.Content),
                    "text/plain",
                    "utf-8")
            }));
        }

        return Task.CompletedTask;
    }
}

internal sealed class RecordingMqttController : IMqttClientController
{
    private readonly object _gate = new();
    private readonly List<MqttPublishMessage> _published = [];

    public string Name => "memory";

    public bool IsConnected => true;

    public MqttTransportCapabilities Capabilities { get; } = new();

    public IReadOnlyList<MqttPublishMessage> Published
    {
        get
        {
            lock (_gate)
                return _published.ToArray();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public ValueTask<MqttClientResult> ExecuteAsync(
        MqttClientRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var publish = request as MqttPublishClientRequest
            ?? throw new InvalidOperationException(
                "The sample controller accepts publish requests only.");
        lock (_gate)
            _published.Add(publish.Message);
        return ValueTask.FromResult<MqttClientResult>(
            new MqttPublishOperationResult(DateTimeOffset.UtcNow, publish.Message));
    }

    public ValueTask<IMqttTriggerRegistration> RegisterTriggerAsync(
        MqttTriggerRegistrationOptions options,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public ValueTask<IMqttClientEventSubscription> SubscribeEventsAsync(
        int capacity = 128,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
