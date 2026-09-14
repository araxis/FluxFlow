using System.Text.Json;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using FluxFlow.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
#if NET10_0_OR_GREATER
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
#endif

var spans = new List<Activity>();
var metrics = new List<Metric>();
var logs = new List<LogRecord>();
#if NET10_0_OR_GREATER
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFluxFlowTraceStream();
#else
var builder = Host.CreateApplicationBuilder(args);
#endif
builder.Services.AddFluxFlowTelemetry()
    .WithTracing(t => t.AddInMemoryExporter(spans))
    .WithMetrics(m => m.AddInMemoryExporter(metrics))
    .WithLogging(l => l.AddInMemoryExporter(logs));
using var host = builder.Build();
#if NET10_0_OR_GREATER
host.MapFluxFlowTraceStream();
if (!((IEndpointRouteBuilder)host).DataSources.SelectMany(s => s.Endpoints).Any())
    throw new InvalidOperationException("The packaged SSE endpoint was not mapped.");
// A fresh port and loopback binding keep this smoke independent of local services.
host.Urls.Add("http://127.0.0.1:0");
#endif
await host.StartAsync();
try
{
    var original = new InvalidOperationException("Package consumer failure", new ArgumentException("inner detail"));
    await using var node = new Probe(original);
    foreach (var value in new[] { 1, 2 })
    {
        await node.Input.SendAsync(FlowMessage.Create(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))))));
        var result = await node.Output.ReceiveAsync(TimeSpan.FromSeconds(10));
        if (result.IsError != (value == 1))
            throw new InvalidOperationException("Error recovery changed across the package boundary.");
    }
    node.Complete();
    await node.Completion.WaitAsync(TimeSpan.FromSeconds(10));
    if (!host.Services.GetRequiredService<TracerProvider>().ForceFlush()
        || !host.Services.GetRequiredService<MeterProvider>().ForceFlush()
        || !host.Services.GetRequiredService<LoggerProvider>().ForceFlush())
        throw new InvalidOperationException("Telemetry flush failed.");
    if (!spans.Any(s => s.Status == ActivityStatusCode.Error && s.Events.Any(e => e.Name == "exception"))
        || !logs.Any(l => ReferenceEquals(l.Exception, original))
        || !metrics.Any(m => m.Name == "fluxflow.processing.completed"))
        throw new InvalidOperationException("A packaged telemetry signal was missing.");
    Console.WriteLine($"PACKAGE_CONSUMER_OK={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
}
finally
{
    await host.StopAsync();
}

sealed class Probe(Exception failure) : FlowTransformNode
{
    protected override Task<FlowMessage> TransformAsync(FlowMessage message)
        => message.Value!.Json.Deserialize<int>() == 1 ? throw failure : Task.FromResult(message);
}
