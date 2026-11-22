using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Http;
using FluxFlow.Configuration.Components.Logging;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Configuration.Components.Mqtt;
using FluxFlow.Core.Nodes;
using FluxFlow.Expressions.Jsonata;
using FluxFlow.Release.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet.Server;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed partial class MqttTests
{
    [Theory]
    [InlineData("Resources:broker:Address", "<MQTT_ADDRESS>", "FluxFlow:Resources:broker", "Address")]
    [InlineData("Resources:api:BaseAddress", "<HTTP_BASE_URL_WITH_TRAILING_SLASH>", "FluxFlow:Resources:api", "BaseAddress")]
    [InlineData("Resources:broker:ClientId", "", "FluxFlow:Resources:broker", "ClientId")]
    [InlineData("Workflows:telemetry:receive:Topics:QoS", "3", "FluxFlow:Workflows:telemetry:receive:Topics", "QoS")]
    [InlineData("Resources:api:Authentication:Token", null, "FluxFlow:Resources:api", "Token")]
    [InlineData("Resources:api:Authentication:Token", "private-trial-token\r\nInjected: value", "FluxFlow:Resources:api", "Token")]
    public void Telemetry_trial_invalid_settings_report_location_without_credentials(
        string setting, string? value, string location, string property)
    {
        var sample = Path.Combine(ReleaseTestPaths.FindRepositoryRoot(), "samples", "Hosting", "FluxFlow.Hosting.Telemetry.Sample");
        using var config = (ConfigurationRoot)new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(sample, "appsettings.json"))
            .AddJsonFile(Path.Combine(sample, "appsettings.trial.template.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FluxFlow:Resources:broker:Address"] = "mqtt://127.0.0.1:1883",
                ["FluxFlow:Resources:api:BaseAddress"] = "http://127.0.0.1:5090/",
                ["FluxFlow:Resources:broker:Username"] = "private-trial-user",
                ["FluxFlow:Resources:broker:Password"] = "private-trial-password",
                ["FluxFlow:Resources:api:Authentication:Scheme"] = "Bearer",
                ["FluxFlow:Resources:api:Authentication:Token"] = "private-trial-token"
            }).Build();
        config["FluxFlow:" + setting] = value;
        var services = new ServiceCollection().AddMqtt().AddHttp().AddMapper().AddLogger().AddJsonataExpressionEngine();
        var before = services.ToArray();
        var error = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowApplication(config.GetRequiredSection("FluxFlow")));
        var diagnostic = error.ToString();
        diagnostic.ShouldContain(location);
        diagnostic.ShouldContain(property);
        diagnostic.ShouldNotContain("private-trial-user");
        diagnostic.ShouldNotContain("private-trial-password");
        diagnostic.ShouldNotContain("private-trial-token");
        services.ToArray().ShouldBe(before);
    }

    [Theory]
    [InlineData(201)]
    [InlineData(422)]
    public async Task Telemetry_configuration_maps_mqtt_readings_and_preserves_http_responses(int status)
    {
        await using var broker = await Broker.Start();
        var received = new BufferBlock<(JsonElement Body, string MessageId)>();
        await using var api = CreateTelemetryApi(async context =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>(context.RequestAborted);
            received.Post((body, context.Request.Headers["X-Flow-Message-Id"].ToString()));
            context.Response.StatusCode = status;
            await context.Response.WriteAsync("downstream-result");
        });
        await api.StartAsync();
        try
        {
            var logs = new TelemetryLogCapture();
            var host = CreateTelemetryHost(broker, api.Urls.Single(), logger: logs);
            await using var owner = (IAsyncDisposable)host;
            using var events = host.Services.ObserveOutput(PortAddress.Parse("telemetry.receive.Events"));
            using var incoming = host.Services.ObserveOutput(PortAddress.Parse("telemetry.receive.Output"));
            using var responses = host.Services.ObserveOutput(PortAddress.Parse("telemetry.send.Output"));
            try
            {
                await host.StartAsync().WaitAsync(Deadline);
                await WaitForTelemetrySubscription(events);
                await broker.Publish("sensors/sensor-7/temperature", Encoding.UTF8.GetBytes("23.5"));
                var original = await incoming.Messages.ReceiveAsync(Deadline);
                var sent = await received.ReceiveAsync(Deadline);
                var response = await responses.Messages.ReceiveAsync(Deadline);
                sent.Body.GetProperty("deviceId").GetString().ShouldBe("sensor-7");
                sent.Body.GetProperty("temperature").GetDecimal().ShouldBe(23.5m);
                sent.Body.GetProperty("unit").GetString().ShouldBe("C");
                sent.MessageId.ShouldBe(original.MessageId.Value);
                sent.Body.GetProperty("messageId").GetString().ShouldBe(original.MessageId.Value);
                sent.Body.GetProperty("traceId").GetString().ShouldBe(original.TraceId.Value);
                response.IsError.ShouldBeFalse(); // HTTP error statuses are ordinary HTTP responses.
                response.Value.Json.GetProperty("StatusCode").GetInt32().ShouldBe(status);
                response.Value.Json.GetProperty("Body").GetString().ShouldBe("downstream-result");
                response.TraceId.ShouldBe(original.TraceId);
            }
            finally { await host.StopAsync().WaitAsync(Deadline); }
            logs.Entries.Count.ShouldBe(0);
        }
        finally { await api.StopAsync().WaitAsync(Deadline); }
    }

    [Fact]
    public async Task Telemetry_mapping_and_transport_errors_do_not_prevent_a_later_reading()
    {
        await using var broker = await Broker.Start();
        var received = new BufferBlock<JsonElement>();
        await using var api = CreateTelemetryApi(async context =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>(context.RequestAborted);
            received.Post(body);
            if (body.GetProperty("deviceId").GetString() == "broken") context.Abort();
            else await context.Response.WriteAsync("accepted");
        });
        await api.StartAsync();
        try
        {
            var logs = new TelemetryLogCapture();
            var host = CreateTelemetryHost(broker, api.Urls.Single(), logger: logs);
            await using var owner = (IAsyncDisposable)host;
            using var events = host.Services.ObserveOutput(PortAddress.Parse("telemetry.receive.Events"));
            using var responses = host.Services.ObserveOutput(PortAddress.Parse("telemetry.send.Output"));
            try
            {
                await host.StartAsync().WaitAsync(Deadline);
                await WaitForTelemetrySubscription(events);
                await broker.Publish("sensors/invalid/temperature", Encoding.UTF8.GetBytes("not-a-number"));
                var mappingError = await responses.Messages.ReceiveAsync(Deadline);
                await AssertLog(mappingError);
                received.Count.ShouldBe(0);

                await broker.Publish("sensors/broken/temperature", Encoding.UTF8.GetBytes("1"));
                var transportError = await responses.Messages.ReceiveAsync(Deadline);
                await AssertLog(transportError);
                (await received.ReceiveAsync(Deadline)).GetProperty("deviceId").GetString().ShouldBe("broken");

                await broker.Publish("sensors/healthy/temperature", Encoding.UTF8.GetBytes("2"));
                var result = await responses.Messages.ReceiveAsync(Deadline);
                result.IsError.ShouldBeFalse();
                result.Value.Json.GetProperty("StatusCode").GetInt32().ShouldBe(200);
                (await received.ReceiveAsync(Deadline)).GetProperty("deviceId").GetString().ShouldBe("healthy");
            }
            finally { await host.StopAsync().WaitAsync(Deadline); }
            logs.Entries.Count.ShouldBe(0); // No duplicate errors or log entry for the healthy reading.

            async Task AssertLog(FluxFlow.Core.Messaging.FlowMessage error)
            {
                error.IsError.ShouldBeTrue();
                var entry = await logs.Entries.ReceiveAsync(Deadline);
                entry.Level.ShouldBe(LogLevel.Error);
                entry.Fields["Code"].ShouldBe(error.Error!.Code);
                entry.Fields["ErrorMessage"].ShouldBe(error.Error.Message);
                entry.Fields["TraceId"].ShouldBe(error.TraceId.Value);
                entry.Fields["MessageId"].ShouldBe(error.MessageId.Value);
                entry.Message.ShouldContain(error.Error.Message);
            }
        }
        finally { await api.StopAsync().WaitAsync(Deadline); }
    }

    [Fact]
    public async Task Telemetry_restores_its_subscription_after_broker_restart_and_disconnects_on_shutdown()
    {
        await using var broker = await Broker.Start();
        await using var api = CreateTelemetryApi(async context =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>(context.RequestAborted);
            await context.Response.WriteAsync(body.GetProperty("deviceId").GetString()!);
        });
        await api.StartAsync();
        try
        {
            var host = CreateTelemetryHost(broker, api.Urls.Single());
            await using var owner = (IAsyncDisposable)host;
            using var events = host.Services.ObserveOutput(PortAddress.Parse("telemetry.receive.Events"));
            using var responses = host.Services.ObserveOutput(PortAddress.Parse("telemetry.send.Output"));
            var resource = host.Services.GetRequiredFlowResource<MqttClientResource>("broker");
            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await host.StartAsync().WaitAsync(Deadline);
                await WaitForTelemetrySubscription(events);
                await AssertDelivery("before");
                await broker.Server.StopAsync();
                await broker.Server.StartAsync();
                await WaitForTelemetrySubscription(events);
                resource.IsConnected.ShouldBeTrue();
                await AssertDelivery("after");
                broker.Server.ClientDisconnectedAsync += args =>
                {
                    if (args.ClientId == broker.ClientId) disconnected.TrySetResult();
                    return Task.CompletedTask;
                };
            }
            finally { await host.StopAsync().WaitAsync(Deadline); }
            await disconnected.Task.WaitAsync(Deadline);
            resource.IsConnected.ShouldBeFalse();
            (await broker.Server.GetClientsAsync()).ShouldNotContain(client => client.Id == broker.ClientId);
            host.Services.GetRequiredKeyedService<FlowWorkflow>("telemetry").Completion.IsCompletedSuccessfully.ShouldBeTrue();

            async Task AssertDelivery(string sensor)
            {
                await broker.Publish($"sensors/{sensor}/temperature", Encoding.UTF8.GetBytes("3"));
                var response = await responses.Messages.ReceiveAsync(Deadline);
                response.IsError.ShouldBeFalse();
                response.Value.Json.GetProperty("StatusCode").GetInt32().ShouldBe(200);
                response.Value.Json.GetProperty("Body").GetString().ShouldBe(sensor);
            }
        }
        finally { await api.StopAsync().WaitAsync(Deadline); }
    }

    private static WebApplication CreateTelemetryApi(RequestDelegate handler)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapPost("/readings", handler);
        return app;
    }

    private static IHost CreateTelemetryHost(Broker broker, string apiAddress,
        IReadOnlyDictionary<string, string?>? overrides = null, ILoggerProvider? logger = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        if (logger is not null) builder.Logging.AddProvider(logger);
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "TelemetrySample.json"));
        builder.Configuration["FluxFlow:Resources:broker:Address"] = $"mqtt://127.0.0.1:{broker.Port}";
        builder.Configuration["FluxFlow:Resources:broker:ClientId"] = broker.ClientId;
        builder.Configuration["FluxFlow:Resources:api:BaseAddress"] = apiAddress + "/";
        if (overrides is not null) builder.Configuration.AddInMemoryCollection(overrides);
        builder.Services.AddMqtt().AddHttp().AddMapper().AddLogger().AddJsonataExpressionEngine()
            .AddFluxFlowApplication(builder.Configuration.GetRequiredSection("FluxFlow")).AddFluxFlow();
        return builder.Build();
    }

    private static async Task WaitForTelemetrySubscription(FlowOutputProbe events)
    {
        using var timeout = new CancellationTokenSource(Deadline);
        while ((await events.Messages.ReceiveAsync(timeout.Token)).Value.Json.GetProperty("Name").GetString() != "mqtt.subscriptions_applied") { }
    }

    private sealed class TelemetryLogCapture : ILoggerProvider, ILogger
    {
        public BufferBlock<(LogLevel Level, string Message, Dictionary<string, object?> Fields)> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => categoryName == "telemetry.log" ? this : NullLogger.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Post((level, formatter(state, exception), ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary()));
        public void Dispose() { }
    }
}
