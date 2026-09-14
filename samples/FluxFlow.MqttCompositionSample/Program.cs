using System.Text.Json;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Composition;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.Definition;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Data;
using FluxFlow.Engine;
using FluxFlow.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var messages = new[]
{
    new SampleMessage("devices/pump-01/state/reply", "ACK: online"),
    new SampleMessage("devices/pump-02/state/reply", "ACK: offline")
};

var configurationPublished = await RunConfigurationCompositionAsync(messages);
PrintPublished("configuration", configurationPublished);

var definitionPublished = await RunDefinitionApplicationAsync(messages);
PrintPublished("definition", definitionPublished);

return 0;

static async Task<IReadOnlyList<MqttPublishMessage>> RunConfigurationCompositionAsync(
    IReadOnlyList<SampleMessage> messages)
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .Build();

    return await RunHostedApplicationAsync(
        messages,
        registerPublishSource: true,
        registerMqtt: true,
        services => services.AddFluxFlowUpdates(
            configuration,
            options => options.StartWithHost = false));
}

static async Task<IReadOnlyList<MqttPublishMessage>> RunDefinitionApplicationAsync(
    IReadOnlyList<SampleMessage> messages)
{
    var application = new ApplicationDefinitionBuilder()
        .AddWorkflow("main", out var workflow);

    application
        .AddMqttBroker(
            "messaging.broker",
            broker =>
            {
                broker.Host = "localhost";
                broker.Port = 443;
                broker.Transport = MqttBrokerTransport.WebSocket;
                broker.UseTls = true;
                broker.WebSocketPath = "/mqtt";
            },
            out var broker)
        .AddMqttRetryPolicy("messaging.retry", out var retry)
        .AddMqttSubscription(
            "messaging.commands",
            subscription =>
            {
                subscription.TopicFilter = "devices/+/state";
                subscription.Qos = MqttQos.AtLeastOnce;
            },
            out var commands)
        .AddMqttClient(
            "messaging.configured",
            client =>
            {
                client.ClientId = "composition-sample";
                client.Broker = broker;
                client.AutoConnect = MqttAutoConnectMode.Disabled;
                client.UseReconnect(retry);
                client.AddSubscription(commands);
            },
            out _)
        .AddExternalResource<IMqttClientController>("messaging.memory", out var runtimeClient);

    workflow
        .AddPublishSource("source",
            out var source)
        .AddMqttPublish(
            "outbound",
            publish =>
            {
                publish.UseClient(runtimeClient);
                publish.MaximumPendingRequests = 16;
            },
            out var outbound);
    outbound.Input.LinkFrom(source.Output);
    source.Definition.Output<FlowValue>("Events").DiscardUnmatched();
    outbound.Output.DiscardUnmatched();
    outbound.Events.DiscardUnmatched();

    var definition = application.Build();

    return await RunHostedApplicationAsync(
        messages,
        registerPublishSource: false,
        registerMqtt: false,
        services => services.AddFluxFlowUpdates(
            definition,
            options => options.StartWithHost = false),
        runtimeClient);
}

static async Task<IReadOnlyList<MqttPublishMessage>> RunHostedApplicationAsync(
    IReadOnlyList<SampleMessage> messages,
    bool registerPublishSource,
    bool registerMqtt,
    Action<IServiceCollection> addApplication,
    ResourceHandle<IMqttClientController>? externalClient = null)
{
    var controller = new RecordingMqttController();
    var services = new ServiceCollection();
    services.AddSingleton(new PublishSourceMessages(messages));
    addApplication(services);
    if (externalClient is not null)
        services.AddExternalFluxFlowResource(externalClient, controller);
    else if (registerMqtt)
        services.AddExternalFluxFlowResource<IMqttClientController>(
            ApplicationAddress.Resource("messaging", "memory"),
            controller);

    if (registerMqtt || registerPublishSource)
    {
        var components = services;
        if (registerMqtt)
            components.AddMqtt();
        if (registerPublishSource)
            components.AddComponent(SampleNodeTypes.PublishSource, SampleComponents.CreatePublishSource);
    }

    await using var provider = services.BuildServiceProvider();
    var application = provider.GetRequiredService<FluxFlowApplication>();
    var result = await application.StartAsync();
    if (result.IsRejected)
    {
        throw new InvalidOperationException(string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Error.Message} {diagnostic.Error.Details}")));
    }

    await WaitForPublishedAsync(controller, messages.Count, TimeSpan.FromSeconds(5));
    await application.StopAsync();
    return controller.Published;
}

static async Task WaitForPublishedAsync(
    RecordingMqttController controller,
    int expectedCount,
    TimeSpan timeout)
{
    using var cancellation = new CancellationTokenSource(timeout);
    while (controller.Published.Count < expectedCount)
        await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token);
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
            System.Text.Encoding.UTF8.GetString(message.Content.Bytes.AsSpan()));
    }
}

internal static class SampleNodeTypes
{
    public const string PublishSource = "sample.mqtt.publish-source";
}

internal static class SampleComponents
{
    public static ValueTask<IFlowNode> CreatePublishSource(ComponentActivationContext context)
    {
        var createdNode = new MqttPublishSourceNode(
                        context.Services.GetRequiredService<PublishSourceMessages>().Messages);
        context.HasOutput(MqttComponentDefinition.Ports.Output, createdNode.Output);
        context.HasEvents(MqttComponentDefinition.Ports.Events, createdNode.Events);
        return ValueTask.FromResult<IFlowNode>(createdNode);
    }

    public static PublishSourceHandle AddPublishSource(this WorkflowDefinitionBuilder workflow, string name)
        => workflow.AddComponent(name, SampleNodeTypes.PublishSource, CreatePublishSource,
            static _ => { }, static component => new PublishSourceHandle(component));

    public static WorkflowDefinitionBuilder AddPublishSource(this WorkflowDefinitionBuilder workflow, string name, out PublishSourceHandle handle)
    {
        handle = workflow.AddPublishSource(name);
        return workflow;
    }
}

internal sealed class PublishSourceHandle(ComponentHandle definition) : AuthoredComponentHandle(definition)
{
    public OutputPortHandle<FlowValue> Output { get; } =
        definition.Output<FlowValue>(MqttComponentDefinition.Ports.Output);
}

internal sealed record SampleMessage(string Topic, string Content);

internal sealed record PublishSourceMessages(IReadOnlyList<SampleMessage> Messages);

internal sealed class MqttPublishSourceNode(IReadOnlyList<SampleMessage> messages)
    : FlowSource
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(
                    FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new MqttPublishMessage
                    {
                        Topic = message.Topic,
                        Content = FlowContent.FromBytes(
                            System.Text.Encoding.UTF8.GetBytes(message.Content),
                            "text/plain",
                            "utf-8")
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))),
                    cancellationToken)
                .ConfigureAwait(false);
        }
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
