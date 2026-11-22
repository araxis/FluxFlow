using System.Threading.Tasks.Dataflow;
using System.Threading.RateLimiting;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using System.Text;
using FluxFlow.Configuration.Components.Scheduling;
using FluxFlow.Configuration.Components.Routing;
using FluxFlow.Configuration.Components.Sources;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Configuration.Components.DataGeneration;
using FluxFlow.Configuration.Components.Mqtt;
using FluxFlow.Configuration.Components.Http;
using FluxFlow.Configuration.Components.Logging;
using FluxFlow.Components.Logging;
using Microsoft.Extensions.Logging;
using FluxFlow.Components.Mqtt;
using FluxFlow.Components.Http;
using FluxFlow.Configuration.Components.Batching;
using FluxFlow.Configuration.Components.Filtering;
using FluxFlow.Configuration.Components.RateLimiting;
using FluxFlow.Configuration.Components.Reducing;
using FluxFlow.Configuration.Components.Resilience;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Components.Batch;
using FluxFlow.Expressions.CSharp;
using FluxFlow.Expressions.Jsonata;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

const string json = """
{
  "resources": { "connection": { "type": "test.connection" }, "limits": { "type": "concurrency", "PermitLimit": 1, "QueueLimit": 8 }, "mqtt": { "type": "mqtt.client", "address": "wss://127.0.0.1/mqtt", "clientId": "configuration-consumer", "autoConnect": false, "reconnect": { "MaxRetryAttempts": 3, "Delay": "00:00:01", "BackoffType": "Exponential", "UseJitter": true, "MaxDelay": "00:00:30" } }, "api": { "type": "http.client", "baseAddress": "https://127.0.0.1/", "timeout": "00:00:05", "headers": { "Accept": "application/json" }, "authentication": { "scheme": "OAuthClientCredentials", "tokenEndpoint": "https://127.0.0.1/token", "clientId": "package-check", "clientSecret": "unused-test-secret" }, "retry": { "MaxRetryAttempts": 2, "Delay": "00:00:00.010" } } },
  "workflows": {
    "demo": {
      "mqttConnection": { "type": "mqtt.connection", "connection": "mqtt" },
      "http": { "type": "http.client", "client": "api" },
      "log": { "type": "logger" },
      "mqttSubscriber": { "type": "mqtt.subscriber", "connection": "mqtt", "topics": ["factory/status", {"Topic":"factory/data","QoS":1}] },
      "mqttPublisher": { "type": "mqtt.publisher", "connection": "mqtt" },
      "clock": { "type": "scheduler", "interval": "00:00:00.050", "output": "samples.Input" },
      "samples": { "type": "source", "values": "inline:[{\"amount\":21}]", "output": "gate.Input" },
      "gate": { "type": "gate", "initiallyOpen": true, "output": "shape.Input" },
      "shape": { "type": "mapper", "engine": "jsonata", "expression": "{'total': amount * 2, 'requestId': $messageId}", "output": { "Address": "select.Input", "Condition": "total = 42" } },
      "select": { "type": "mapper", "engine": "csharp", "expression": "base64:dmFsdWUudG90YWw=", "output": "route.Input" },
      "route": { "type": "router", "engine": "csharp", "routes": { "Accepted": "value == 42 && messageId != null && traceId != null" }, "Accepted": "changes.Input" },
      "changes": { "type": "changeFilter", "engine": "jsonata", "keySelector": "$string($value)", "output": "unique.Input" },
      "unique": { "type": "deduplicate", "engine": "jsonata", "keySelector": "$string($value)", "retention": "00:00:10", "output": "sum.Input" },
      "sum": { "type": "reducer", "engine": "csharp", "initialState": "0", "reduce": "state + input", "areEqual": "previous == current", "output": "generate.Input" },
      "generate": { "type": "dataGenerator", "expression": "$value + $fakeInt(0, 0)", "output": "limit.Input" },
      "limit": { "type": "rateLimit", "limiter": "limits", "responseTimeout": "00:00:05", "request": "retry.Input", "output": "collect.Input" },
      "retry": { "type": "retry", "delay": "00:00:00", "request": "echo.Input", "reply": "echo.Output", "output": "limit.Reply" },
      "echo": { "type": "mapper", "engine": "jsonata", "expression": "$" },
      "collect": { "type": "batch", "batchSize": 1, "maxWait": "00:00:05", "output": "sink.Input" },
      "sink": { "type": "sink", "Input": "collect.Output" }
    }
  }
}
""";
using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
using var configurationLifetime = (IDisposable)configuration;
var shape = configuration.GetSection("workflows:demo:shape").ReadConfiguration<MapperConfiguration>()!;
if (shape.Output.Single().Address.Value != "demo.select.Input" || shape.Output[0].Condition != "total = 42" || shape.Expression!.Read(AppContext.BaseDirectory) != "{'total': amount * 2, 'requestId': $messageId}")
    throw new Exception("Typed configuration values must bind through the packaged API.");
var received = new TaskCompletionSource<FlowMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
var services = new ServiceCollection().AddScheduler().AddGate().AddSource().AddMapper().AddRouter().AddBatch()
    .AddChangeFilter().AddDeduplicate().AddReducer().AddRateLimit().AddRetry().AddDataGenerator().AddMqtt().AddHttp().AddLogger()
    .AddCSharpExpressionEngine().AddJsonataExpressionEngine(useAsDefault: true);
services.AddKeyedSingleton<ResourceRegistration>("concurrency", (registrations, address, section) =>
{
    var options = section.ReadRequiredConfiguration<ConcurrencyLimiterOptions>();
    registrations.AddFluxFlowResource<RateLimiter>(address, _ => new ConcurrencyLimiter(options));
});
var cleanup = new List<string>();
var resourcesCreated = 0;
var resourceRegistrations = 0;
services.AddKeyedSingleton<ResourceRegistration>("test.connection", (resources, address, _) =>
{
    resourceRegistrations++;
    resources.AddFluxFlowResource(address, _ =>
    {
        resourcesCreated++;
        return new Connection(cleanup);
    });
});
services.AddKeyedSingleton<ComponentRegistration>("sink", (registrations, section) =>
{
    var address = section.GetComponentAddress();
    var config = section.ReadRequiredConfiguration<SinkConfiguration>();
    registrations.AddComponentFactory(ComponentAddress.Parse(address.Value), dependencies =>
    {
        return new Sink(received, dependencies.GetRequiredFlowResource<Connection>("connection", "demo"), cleanup);
    });
    registrations.AddInputPort<Sink>(ComponentAddress.Parse(address.Value), "Input", node => node.Input);
    if (config.Input is { } input)
        registrations.AddInputLink(address.Port("Input"), input);
});
services.AddFluxFlowApplication(configuration);
services.AddLink(PortAddress.Parse("demo.collect.Output"), PortAddress.Parse("demo.sink.Input"));
if (services.Count(item => item.ServiceType == typeof(LinkFactory) && !item.IsKeyedService) != 16)
    throw new Exception("Direct, input-side and output-side declarations must share one link registration.");
if (resourcesCreated != 0 || resourceRegistrations != 1)
    throw new Exception("Configuration must register factories without constructing resources.");
await using var provider = services.BuildServiceProvider();
var workflow = provider.GetRequiredKeyedService<FlowWorkflow>("demo");
if (resourcesCreated != 0)
    throw new Exception("Workflow discovery must not construct components or resources.");
await using var app = await provider.BuildApplicationAsync();
if (provider.GetRequiredFlowResource<MqttClientResource>("mqtt").IsConnected)
    throw new Exception("Building MQTT components must not connect to a broker.");
var httpClient = provider.GetRequiredFlowResource<System.Net.Http.HttpClient>("api");
if (httpClient.BaseAddress?.ToString() != "https://127.0.0.1/" || httpClient.Timeout != TimeSpan.FromSeconds(5))
    throw new Exception("HTTP resource settings must reach the shared native client.");
_ = provider.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.http.Input");
_ = provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.http.Output");
_ = provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.http.Events");
_ = provider.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.log.Input");
_ = provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.log.Output");
_ = provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.log.Events");
var logRequest = new LogRequest { LogLevel = LogLevel.Information, MessageTemplate = "Order {Id}", Args = ["A12"] };
if (logRequest.Json.GetProperty("Args")[0].GetString() != "A12")
    throw new Exception("Log request JSON must retain typed arguments.");
var httpRequest = new HttpSendRequest
{
    RequestUrl = "orders", Method = "POST",
    Body = new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(new { quantity = 2 }))
};
if (httpRequest.Json.GetProperty("Body").GetProperty("quantity").GetInt32() != 2)
    throw new Exception("HTTP request JSON must preserve the structured body.");
if (resourcesCreated != 1 || resourceRegistrations != 1)
    throw new Exception("Build must resolve resources without running registration again.");
if (!ReferenceEquals(app.Workflows.Single(item => item.Name == "demo"), workflow))
    throw new Exception("Build must reuse the DI workflow instance.");
if (received.Task.IsCompleted)
    throw new Exception("Build must not start sources.");
await app.StartAsync();
_ = app.GetComponent(ComponentAddress.Parse("demo.clock"));
var tick = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
if (tick.IsError || tick.Value is not FlowBatch batch || batch.Items.Count != 1 || batch.Items[0].Value.Json.GetInt32() != 42)
    throw new Exception("Unexpected batched mapping result.");
await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
await app.Completion.WaitAsync(TimeSpan.FromSeconds(10));
if (!cleanup.SequenceEqual(new[] { "node" }))
    throw new Exception("Workflow scope disposal must release the node while the shared resource remains owned by the host.");
await provider.DisposeAsync();
if (!cleanup.SequenceEqual(new[] { "node", "resource" }))
    throw new Exception("Resource must outlive component cleanup.");
await TelemetryCheck.RunAsync();
Console.WriteLine($"CONFIGURATION_CONSUMER_OK={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
sealed class SinkConfiguration
{
    public PortReference? Input { get; init; }
}

sealed class Sink : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }

    public Sink(TaskCompletionSource<FlowMessage> received, Connection connection, List<string> cleanup)
    {
        var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
        var outputCapacityValue = 128;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
        var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
        Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping), execution);
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        this.received = received;
        this.connection = connection;
        this.cleanup = cleanup;
        Lifetime.Observe();
    }

    private readonly List<string> cleanup;
    private readonly Connection connection;
    private readonly TaskCompletionSource<FlowMessage> received;
    private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        if (connection.Disposed)
            throw new Exception("Resource disposed during processing.");
        received.TrySetResult(message);
        return Task.FromResult<IEnumerable<FlowMessage>>([]);
    }

    protected override ValueTask OnDisposeAsync()
    {
        cleanup.Add("node");
        return ValueTask.CompletedTask;
    }
}

sealed class Connection(List<string> cleanup) : IAsyncDisposable
{
    public bool Disposed;
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        cleanup.Add("resource");
        return ValueTask.CompletedTask;
    }
}
