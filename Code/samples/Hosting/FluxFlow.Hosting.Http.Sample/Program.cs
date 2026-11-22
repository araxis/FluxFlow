using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Http;
using FluxFlow.Configuration.Components.Logging;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Configuration.Components.Mqtt;
using FluxFlow.Configuration.Components.Serial;
using FluxFlow.Expressions.Jsonata;
using FluxFlow.Hosting.AspNetCore;
using System.Net;
using System.Text;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.Services.AddHttpTriggers()
    .AddHttp()
    .AddMapper()
    .AddMqtt()
    .AddSerial()
    .AddLogger()
    .AddJsonataExpressionEngine()
    .AddFluxFlowApplication(builder.Configuration.GetRequiredSection(builder.Configuration["WorkflowSection"] ?? "FluxFlow"))
    .AddFluxFlow();

await using var app = builder.Build();
app.MapFluxFlowHttpTriggers();
if (!builder.Configuration.GetValue<bool>("DemonstrateDrain"))
{
    await app.RunAsync();
    return;
}

try
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await app.StartAsync(deadline.Token);
    using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(10) };
    var workflow = app.Services.GetRequiredKeyedService<FlowWorkflow>("api");
    await CheckReadingAsync(HttpStatusCode.OK);
    Console.WriteLine("Stopping the api workflow; its trigger is configured to drain accepted requests.");
    await workflow.StopAsync(deadline.Token);
    await CheckReadingAsync(HttpStatusCode.ServiceUnavailable);
    Console.WriteLine("Restarting api with the same configuration and routes.");
    await workflow.StartAsync(deadline.Token);
    await CheckReadingAsync(HttpStatusCode.OK);

    async Task CheckReadingAsync(HttpStatusCode expected)
    {
        using var body = new StringContent("{\"value\":23.5,\"unit\":\"C\"}", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/devices/sensor-7/readings", body, deadline.Token);
        if (response.StatusCode != expected)
            throw new InvalidOperationException($"Expected HTTP {(int)expected}, received {(int)response.StatusCode}.");
        Console.WriteLine($"POST /devices/sensor-7/readings -> {(int)response.StatusCode}");
    }
}
finally
{
    using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await app.StopAsync(shutdown.Token);
}
Console.WriteLine("HTTP_DRAIN_SAMPLE_VERIFIED: 200, stopped 503, restarted 200 and host shutdown.");
