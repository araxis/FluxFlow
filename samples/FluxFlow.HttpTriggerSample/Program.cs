using FluxFlow.Components.Http.AspNetCore;
using FluxFlow.HttpTriggerSample;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Observability;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);
var telemetry = builder.Services.AddFluxFlowTelemetry()
    .ConfigureResource(resource => resource.AddService("FluxFlow.HttpTriggerSample"))
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());
// Export only when the host explicitly configures a collector endpoint.
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
    telemetry.UseOtlpExporter();
builder.Services.AddFluxFlowTraceStream();

// Register the HTTP trigger as a component and wire its graph. No engine, no registry:
// the trigger gets its request source (keyed) injected and uses a request/reply
// coordinator internally; here we just link its ports to a hand-written GreetingNode.
builder.Services.AddFluxFlowHttpTrigger("greet", trigger =>
{
    var greeting = new GreetingNode();
    trigger.Output.LinkTo(greeting.Input, new DataflowLinkOptions { PropagateCompletion = false });
    greeting.Output.LinkTo(trigger.Responses, new DataflowLinkOptions { PropagateCompletion = false });
});

var app = builder.Build();

// The endpoint just feeds the keyed trigger; the coordinator writes the reply back.
app.MapFluxFlowTrigger("/greet", "greet");
// Local demonstration only. Production hosts should apply their authorization policy.
app.MapFluxFlowTraceStream().AddEndpointFilter(async (context, next) =>
    context.HttpContext.Connection.RemoteIpAddress is { } address && System.Net.IPAddress.IsLoopback(address)
        ? await next(context) : Results.StatusCode(403));
app.MapGet("/", () => Results.Text("POST a name to /greet, e.g.  curl -d Ada http://localhost:5000/greet"));

app.Run();
