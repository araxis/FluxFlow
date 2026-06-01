using FluxFlow.Components.Control;
using FluxFlow.Components.Mqtt;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mapping;
using FluxFlow.Engine;
using FluxFlow.Engine.Runtime;
using FluxFlow.MqttCompositionSample;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

var adapter = new InMemoryMqttClientAdapter(CreateSeedMessages());
var clientFactory = new InMemoryMqttClientFactory(adapter);
var store = new SampleStore();
var expressionEngine = new SampleExpressionEngine();

var registry = new RuntimeNodeFactoryRegistry()
    .RegisterMqttComponents(clientFactory)
    .RegisterMappingComponents(options => options
        .UseExpressionEngine(expressionEngine)
        .RegisterType<MqttReceivedMessage>("sample.mqtt.received")
        .RegisterType<OrderMessage>("sample.order")
        .RegisterType<MqttPublishRequest>("sample.mqtt.publish")
        .UseContextFactory(new MqttMessageContextFactory())
        .UseContextFactory(new OrderMessageContextFactory()))
    .RegisterControlComponents(options => options
        .UseExpressionEngine(expressionEngine)
        .RegisterType<OrderMessage>("sample.order")
        .UseContextFactory(new OrderMessageContextFactory()))
    .RegisterSampleComponents(store);

await using var host = FlowApplicationHost.Create(SampleApplicationDefinition.Create(), registry);

var build = host.Build();
if (!build.IsSuccess || host.Runtime is null)
{
    foreach (var error in build.Errors)
    {
        Console.Error.WriteLine(error.Message);
    }

    return 1;
}

var diagnostics = new BufferBlock<RuntimeFlowDiagnostic>(
    new DataflowBlockOptions { BoundedCapacity = 32 });
host.Runtime.Diagnostics.LinkTo(
    diagnostics,
    new DataflowLinkOptions { PropagateCompletion = true });

var start = await host.StartBuiltAsync();
if (!start.IsSuccess)
{
    foreach (var error in start.Errors)
    {
        Console.Error.WriteLine(error.Message);
    }

    return 1;
}

await host.Runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
var observedDiagnostics = await ReceiveUntilCompletedAsync(diagnostics, TimeSpan.FromSeconds(5));
var published = adapter.Published
    .OrderBy(request => request.Topic, StringComparer.Ordinal)
    .ToArray();
var results = store.GetResults()
    .OrderBy(result => result.Topic, StringComparer.Ordinal)
    .ToArray();

Console.WriteLine("Sample: mqtt-composition");
Console.WriteLine($"Factory contexts: {clientFactory.Contexts.Count}");
Console.WriteLine($"Published messages: {published.Length}");
foreach (var request in published)
{
    Console.WriteLine(
        string.Create(
            CultureInfo.InvariantCulture,
            $"{request.Topic} bytes={request.Payload.Length} qos={request.QualityOfService} retain={request.Retain} correlation={request.CorrelationId}"));
}

Console.WriteLine();
Console.WriteLine($"Results observed: {results.Length}");
Console.WriteLine($"Diagnostics observed: {observedDiagnostics.Count}");

return 0;

static IReadOnlyList<MqttReceivedMessage> CreateSeedMessages()
{
    var now = DateTimeOffset.UtcNow;
    return
    [
        CreateMessage(now, new IncomingOrder("A-100", "Harbor Market", 125m, Active: true), "c-100"),
        CreateMessage(now, new IncomingOrder("A-101", "Cedar Supply", 42m, Active: false), "c-101"),
        CreateMessage(now, new IncomingOrder("A-102", "Summit Works", 230m, Active: true), "c-102")
    ];
}

static MqttReceivedMessage CreateMessage(
    DateTimeOffset timestamp,
    IncomingOrder order,
    string correlationId)
{
    var payload = JsonSerializer.SerializeToUtf8Bytes(order, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    return new MqttReceivedMessage
    {
        Timestamp = timestamp,
        Topic = "orders/input",
        Payload = payload,
        PayloadPreview = Encoding.UTF8.GetString(payload),
        ContentType = "application/json",
        QualityOfService = MqttQualityOfService.AtLeastOnce,
        Retain = false,
        CorrelationId = correlationId
    };
}

static async Task<IReadOnlyList<T>> ReceiveUntilCompletedAsync<T>(
    IReceivableSourceBlock<T> source,
    TimeSpan timeout)
{
    using var timeoutToken = new CancellationTokenSource(timeout);
    var values = new List<T>();
    try
    {
        while (await source.OutputAvailableAsync(timeoutToken.Token).ConfigureAwait(false))
        {
            while (source.TryReceive(out var value))
            {
                values.Add(value);
            }
        }
    }
    catch (OperationCanceledException) when (timeoutToken.IsCancellationRequested)
    {
        throw new TimeoutException("Timed out while reading sample output.");
    }

    return values;
}
