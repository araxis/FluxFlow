using System.Text;
using FluxFlow.Configuration.Scheduling;
using FluxFlow.Configuration.Routing;
using FluxFlow.Configuration.Sources;
using FluxFlow.Configuration.Mapping;
using FluxFlow.Configuration.Batching;
using FluxFlow.Components.Batch;
using FluxFlow.Expressions.CSharp;
using FluxFlow.Expressions.Jsonata;
using FluxFlow.Configuration;
using FluxFlow.Core.Application;

using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

const string json = """
{
  "resources": { "connection": { "type": "test.connection" } },
  "workflows": {
    "demo": {
      "clock": { "type": "scheduler", "interval": "00:00:00.050", "output": "samples.Input" },
      "samples": { "type": "source", "valuesJson": "[{\"amount\":21}]", "output": "gate.Input" },
      "gate": { "type": "gate", "initiallyOpen": true, "output": "shape.Input" },
      "shape": { "type": "mapper", "engine": "jsonata", "expression": "{'total': amount * 2, 'requestId': $messageId}", "output": { "Port": "select.Input", "Condition": "total = 42" } },
      "select": { "type": "mapper", "engine": "csharp", "expression": "base64:dmFsdWUudG90YWw=", "output": "route.Input" },
      "route": { "type": "router", "engine": "csharp", "routes": { "Accepted": "value == 42 && messageId != null && traceId != null" }, "Accepted": "collect.Input" },
      "collect": { "type": "batch", "batchSize": 1, "maxWait": "00:00:05", "output": "sink.Input" },
      "sink": { "type": "sink" }
    }
  }
}
""";
using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
using var configurationLifetime = (IDisposable)configuration;
var shape = configuration.GetSection("workflows:demo:shape").ReadConfiguration<MapperConfiguration>()!;
if (shape.Output.Single().Port != "select.Input" || shape.Output[0].Condition != "total = 42"
    || shape.Expression!.Read(AppContext.BaseDirectory) != "{'total': amount * 2, 'requestId': $messageId}")
    throw new Exception("Typed configuration values must bind through the packaged API.");
var received = new TaskCompletionSource<FlowMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
var services = new ServiceCollection().AddScheduler().AddGate().AddSource()
    .AddMapper().AddRouter().AddBatch().AddCSharpExpressionEngine().AddJsonataExpressionEngine(useAsDefault: true);
var cleanup = new List<string>();
var resourcesCreated = 0;
var resourceRegistrations = 0;
services.AddKeyedSingleton<ResourceRegistration>("test.connection", (resources, address, _) =>
{
    resourceRegistrations++;
    resources.AddFluxFlowResource(address, _ => { resourcesCreated++; return new Connection(cleanup); });
});
services.AddKeyedSingleton<ComponentRegistration>("sink", (registrations, section) =>
{
    var address = section.GetComponentAddress();
    registrations.AddComponentFactory(address, dependencies =>
    {
        return new Sink(received,
            dependencies.GetRequiredFlowResource<Connection>("connection", "demo"), cleanup);
    });
    registrations.AddInputPort<Sink>(address, "Input", node => node.Input);
});
await services.AddFluxFlowApplicationAsync(configuration);
if (resourcesCreated != 0 || resourceRegistrations != 1) throw new Exception("Configuration must register factories without constructing resources.");
await using var provider = services.BuildServiceProvider();
await using var app = await provider.BuildApplicationAsync();
if (resourcesCreated != 1 || resourceRegistrations != 1) throw new Exception("Build must resolve resources without running registration again.");
if (!app.Workflows.ContainsKey("demo")) throw new Exception("Build must construct the workflow.");
if (received.Task.IsCompleted) throw new Exception("Build must not start sources.");
await app.StartAsync();
_ = app.GetComponent("demo.clock");
var tick = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
if (tick.IsError || tick.Value is not FlowBatch batch || batch.Items.Count != 1 ||
    batch.Items[0].Value.Json.GetInt32() != 42) throw new Exception("Unexpected batched mapping result.");
await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
await app.Completion.WaitAsync(TimeSpan.FromSeconds(10));
if (cleanup.Count != 0) throw new Exception("Application must not dispose DI-owned instances.");
await provider.DisposeAsync();
if (!cleanup.SequenceEqual(new[] { "node", "resource" })) throw new Exception("Resource must outlive component cleanup.");
Console.WriteLine($"SCHEDULER_CONSUMER_OK={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");

sealed class Sink(TaskCompletionSource<FlowMessage> received, Connection connection, List<string> cleanup) : FlowTransformNode
{
    protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        if (connection.Disposed) throw new Exception("Resource disposed during processing.");
        received.TrySetResult(message);
        return Task.FromResult<IEnumerable<FlowMessage>>([]);
    }
    protected override ValueTask OnDisposeAsync() { cleanup.Add("node"); return ValueTask.CompletedTask; }
}

sealed class Connection(List<string> cleanup) : IAsyncDisposable
{
    public bool Disposed;
    public ValueTask DisposeAsync() { Disposed = true; cleanup.Add("resource"); return ValueTask.CompletedTask; }
}
