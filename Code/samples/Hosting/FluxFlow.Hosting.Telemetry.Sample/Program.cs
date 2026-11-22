using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Http;
using FluxFlow.Configuration.Components.Logging;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Configuration.Components.Mqtt;
using FluxFlow.Configuration.Components.Serial;
using FluxFlow.Expressions.Jsonata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.Services.AddMqtt()
    .AddSerial()
    .AddHttp()
    .AddMapper()
    .AddLogger()
    .AddJsonataExpressionEngine()
    .AddFluxFlowApplication(builder.Configuration.GetRequiredSection(builder.Configuration["WorkflowSection"] ?? "FluxFlow"))
    .AddFluxFlow();

using var shutdown = new CancellationTokenSource();
if (builder.Configuration.GetValue<TimeSpan?>("RunFor") is { } duration)
{
    if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("RunFor", "RunFor must be positive.");
    shutdown.CancelAfter(duration);
}
await builder.Build().RunAsync(shutdown.Token);
