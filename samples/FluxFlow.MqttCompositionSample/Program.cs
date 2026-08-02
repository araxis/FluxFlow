using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Composition.Model;
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
        services => services.AddFluxFlow(
            configuration,
            options => options.StartWithHost = false));
}

static async Task<IReadOnlyList<MqttPublishMessage>> RunDefinitionApplicationAsync(
    IReadOnlyList<SampleMessage> messages)
{
    var application = new ApplicationDefinitionBuilder()
        .AddResourceGroup("messaging", out var messaging)
        .AddWorkflow("main", out var workflow);

    messaging
        .AddMqttBroker(
            "broker",
            broker =>
            {
                broker.Host = "localhost";
                broker.Port = 1883;
            },
            out var broker)
        .AddMqttRetryPolicy("retry", out var retry)
        .AddMqttSubscription(
            "commands",
            subscription =>
            {
                subscription.TopicFilter = "devices/+/state";
                subscription.Qos = MqttQos.AtLeastOnce;
            },
            out var commands)
        .AddMqttClient(
            "configured",
            client =>
            {
                client.ClientId = "composition-sample";
                client.Broker = broker;
                client.AutoConnect = MqttAutoConnectMode.Disabled;
                client.UseReconnect(retry);
                client.AddSubscription(commands);
            },
            out _)
        .AddExternalResource<IMqttClientController>("memory", out var runtimeClient);

    workflow
        .AddComponent(
            "source",
            SampleNodeTypes.PublishSource,
            out var source)
        .AddMqttPublish(
            "outbound",
            publish =>
            {
                publish.UseClient(runtimeClient);
                publish.MaximumPendingRequests = 16;
            },
            out var outbound)
        .Connect(
            source.Output<MqttPublishMessage>(MqttComponentDefinition.Ports.Output),
            outbound.Input);

    var definition = application.Build();
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .Build();
    var configuredDefinition = await new ConfigurationApplicationDefinitionSource(configuration)
        .LoadAsync();
    if (!string.Equals(
            ApplicationDefinitionJson.Serialize(configuredDefinition),
            ApplicationDefinitionJson.Serialize(definition),
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "The JSON and fluent C# definitions must have the same canonical form.");
    }

    return await RunHostedApplicationAsync(
        messages,
        services => services.AddFluxFlow(
            definition,
            options => options.StartWithHost = false));
}

static async Task<IReadOnlyList<MqttPublishMessage>> RunHostedApplicationAsync(
    IReadOnlyList<SampleMessage> messages,
    Action<IServiceCollection> addApplication)
{
    var controller = new RecordingMqttController();
    var services = new ServiceCollection();
    addApplication(services);
    services.AddFluxFlowComponents()
        .AddMqtt()
        .AddRuntimeComponent(SampleNodeTypes.PublishSource, component =>
        {
            component.UseFactory(_ =>
            {
                var node = new MqttPublishSourceNode(messages);
                return ValueTask.FromResult(ComponentInstance.Create(
                    node,
                    outputs:
                    [
                        ComponentPorts.Output<MqttPublishMessage>(
                            MqttComponentDefinition.Ports.Output,
                            node.Output)
                    ],
                    events: node.Events));
            });
            component.AddOutput<MqttPublishMessage>(MqttComponentDefinition.Ports.Output);
        });
    services.AddApplicationResourceRegistrar(new SampleResourceRegistrar(controller));

    await using var provider = services.BuildServiceProvider();
    var application = provider.GetRequiredService<FluxFlowApplication>();
    var result = await application.StartAsync();
    if (result.IsRejected)
    {
        throw new InvalidOperationException(string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(diagnostic => diagnostic.Error.Message)));
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

internal sealed record SampleMessage(string Topic, string Content);

internal sealed class SampleResourceRegistrar(IMqttClientController controller)
    : IApplicationResourceRegistrar
{
    public void Register(ApplicationResourceRegistrationContext context)
        => context.Services.AddExternalFluxFlowResource<IMqttClientController>(
            ApplicationAddress.Resource("messaging", "memory"),
            controller);
}

internal sealed class MqttPublishSourceNode(IReadOnlyList<SampleMessage> messages)
    : FlowSource<MqttPublishMessage>
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(
                    FlowMessage.Create(new MqttPublishMessage
                    {
                        Topic = message.Topic,
                        Content = FlowContent.FromBytes(
                            System.Text.Encoding.UTF8.GetBytes(message.Content),
                            "text/plain",
                            "utf-8")
                    }),
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
