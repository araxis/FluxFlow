using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Components.Mqtt;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Http;
using FluxFlow.Configuration.Components.Logging;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Configuration.Components.Mqtt;
using FluxFlow.Expressions.Jsonata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Server;

internal static class TelemetryCheck
{
    internal static async Task RunAsync()
    {
        var deadline = TimeSpan.FromSeconds(15);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var broker = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(port)
            .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback).Build());
        await broker.StartAsync();
        try
        {
            await broker.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
                .WithTopic("sensors/package-check/temperature").WithPayload("23.5").WithRetainFlag().Build()));
            var received = new TaskCompletionSource<(JsonElement Body, string MessageId)>(TaskCreationOptions.RunContinuationsAsynchronously);
            var apiBuilder = WebApplication.CreateSlimBuilder();
            apiBuilder.Logging.ClearProviders();
            apiBuilder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var api = apiBuilder.Build();
            api.MapPost("/readings", async (HttpContext context) =>
            {
                var body = await context.Request.ReadFromJsonAsync<JsonElement>(context.RequestAborted);
                received.TrySetResult((body, context.Request.Headers["X-Flow-Message-Id"].ToString()));
                context.Response.StatusCode = StatusCodes.Status201Created;
            });
            await api.StartAsync();
            try
            {
                var builder = Host.CreateApplicationBuilder();
                builder.Logging.ClearProviders();
                builder.Configuration.Sources.Clear();
                builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "TelemetrySample.json"));
                var clientId = Guid.NewGuid().ToString("N");
                builder.Configuration["FluxFlow:Resources:broker:Address"] = $"mqtt://127.0.0.1:{port}";
                builder.Configuration["FluxFlow:Resources:broker:ClientId"] = clientId;
                builder.Configuration["FluxFlow:Resources:api:BaseAddress"] = api.Urls.Single() + "/";
                builder.Services.AddMqtt().AddHttp().AddMapper().AddLogger().AddJsonataExpressionEngine()
                    .AddFluxFlowApplication(builder.Configuration.GetRequiredSection("FluxFlow")).AddFluxFlow();
                var host = builder.Build();
                await using var owner = (IAsyncDisposable)host;
                using var responses = host.Services.ObserveOutput(PortAddress.Parse("telemetry.send.Output"));
                try
                {
                    await host.StartAsync().WaitAsync(deadline);
                    var response = await responses.Messages.ReceiveAsync(deadline);
                    if (response.IsError || response.Value.Json.GetProperty("StatusCode").GetInt32() != 201)
                        throw new Exception($"Packaged telemetry HTTP request failed: {response.Value.Json}");
                    var reading = await received.Task.WaitAsync(deadline);
                    if (reading.Body.GetProperty("deviceId").GetString() != "package-check"
                        || reading.Body.GetProperty("temperature").GetDecimal() != 23.5m
                        || reading.Body.GetProperty("unit").GetString() != "C"
                        || string.IsNullOrWhiteSpace(reading.MessageId)
                        || reading.Body.GetProperty("messageId").GetString() != reading.MessageId
                        || reading.Body.GetProperty("traceId").GetString() != response.TraceId.Value)
                        throw new Exception("Packaged telemetry mapping or message context was not preserved.");
                }
                finally { await host.StopAsync().WaitAsync(deadline); }
                if (host.Services.GetRequiredFlowResource<MqttClientResource>("broker").IsConnected
                    || (await broker.GetClientsAsync()).Any(client => client.Id == clientId))
                    throw new Exception("Packaged telemetry host did not disconnect MQTT on shutdown.");
            }
            finally { await api.StopAsync().WaitAsync(deadline); }
        }
        finally { await broker.StopAsync(); }
        Console.WriteLine($"TELEMETRY_PACKAGE_CONSUMER_OK={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
    }
}
