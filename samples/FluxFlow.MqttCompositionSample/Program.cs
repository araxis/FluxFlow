using System.Text.Json;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Hosting;
using FluxFlow.Composition.Hosting.DependencyInjection;
using FluxFlow.Composition.Model;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ApplicationWorkflowDefinition = FluxFlow.Composition.Model.WorkflowDefinition;

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
        services => services.AddFluxFlowApplication(configuration));
}

static async Task<IReadOnlyList<MqttPublishMessage>> RunDefinitionApplicationAsync(
    IReadOnlyList<SampleMessage> messages)
{
    var definition = new ApplicationDefinition(
        resources:
        [
            new("memory", new ResourceInstanceDefinition("host.external"))
        ],
        workflows:
        [
            new("main", new ApplicationWorkflowDefinition(
            [
                new("source", Component(
                    SampleNodeTypes.PublishSource,
                    (MqttComponentPortNames.Output, "outbound.Input"))),
                new("outbound", Component(
                    MqttComponentTypes.Publish,
                    (MqttComponentResourceNames.Client, "Resources.memory"),
                    ("maximumPendingRequests", 16)))
            ]))
        ]);

    return await RunHostedApplicationAsync(
        messages,
        services => services.AddFluxFlowApplication(definition));
}

static async Task<IReadOnlyList<MqttPublishMessage>> RunHostedApplicationAsync(
    IReadOnlyList<SampleMessage> messages,
    Action<IServiceCollection> addApplication)
{
    var controller = new RecordingMqttController();
    var services = new ServiceCollection();
    addApplication(services);
    services
        .AddFluxFlowEngine()
        .AddMqttComponents()
        .AddFluxFlowComponent(CreatePublishSourceDescriptor(messages))
        .AddApplicationResourceRegistrar(new SampleResourceRegistrar(controller));

    await using var provider = services.BuildServiceProvider();
    var host = provider.GetRequiredService<IApplicationRevisionHost>();
    var result = await host.StartApplicationAsync();
    if (!result.Succeeded)
    {
        throw new InvalidOperationException(string.Join(
            Environment.NewLine,
            result.Update!.Failures.Select(failure => failure.Error.Message)));
    }

    await WaitForPublishedAsync(controller, messages.Count, TimeSpan.FromSeconds(5));
    await host.StopApplicationAsync();
    return controller.Published;
}

static ComponentDefinition Component(
    string type,
    params (string Name, object? Value)[] properties)
    => new(
        type,
        properties.Select(property => KeyValuePair.Create(
            property.Name,
            JsonSerializer.SerializeToElement(property.Value))));

static async Task WaitForPublishedAsync(
    RecordingMqttController controller,
    int expectedCount,
    TimeSpan timeout)
{
    using var cancellation = new CancellationTokenSource(timeout);
    while (controller.Published.Count < expectedCount)
        await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token);
}

static ComponentDescriptor CreatePublishSourceDescriptor(
    IReadOnlyList<SampleMessage> messages)
    => new(
            SampleNodeTypes.PublishSource,
            _ =>
            {
                var node = new MqttPublishSourceNode(messages);
                return ValueTask.FromResult(ComponentInstance.Create(
                    node,
                    outputs:
                    [
                        ComponentPorts.Output<MqttPublishMessage>(
                            MqttComponentPortNames.Output,
                            node.Output)
                    ],
                    events: node.Events));
            },
            outputs:
            [
                ComponentPorts.Metadata<MqttPublishMessage>(
                    MqttComponentPortNames.Output)
            ]);

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
            ApplicationAddress.Resource("memory"),
            controller);
}

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
