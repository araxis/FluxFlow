using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Components.Http;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Http;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Configuration.Components.Mqtt;
using FluxFlow.Core.Nodes;
using FluxFlow.Expressions.Jsonata;
using FluxFlow.Hosting.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed partial class MqttTests
{
    [Fact]
    public async Task Http_commands_publish_payloads_and_correlate_concurrent_replies()
    {
        await using var broker = await Broker.Start();
        var publications = new BufferBlock<(string Topic, JsonElement Body)>();
        broker.Server.InterceptingPublishAsync += args =>
        {
            var message = args.ApplicationMessage;
            message.QualityOfServiceLevel.ShouldBe(MqttQualityOfServiceLevel.AtLeastOnce);
            message.Retain.ShouldBeFalse();
            publications.Post((message.Topic, JsonSerializer.Deserialize<JsonElement>(message.ConvertPayloadToString())));
            return Task.CompletedTask;
        };
        await using var app = CreateCommandHost(broker);
        var trigger = CommandTrigger(app);
        var events = Capture(((MqttConnection)app.Services.GetWorkflowServices("commands").GetRequiredKeyedService<IFlowNode>("commands.connection")).Events);
        try
        {
            await app.StartAsync().WaitAsync(Deadline);
            await WaitForEvent(events, "mqtt.connected");
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = Deadline };
            var replies = await Task.WhenAll(Enumerable.Range(1, 6).Select(async index =>
            {
                using var response = await client.PostAsJsonAsync($"/devices/motor-{index}/commands",
                    new { Command = "Move", Arguments = new { Position = index } }, JsonSerializerOptions.Default);
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                body.GetProperty("Topic").GetString().ShouldBe($"devices/motor-{index}/commands");
                // Broker acknowledgement is successful even without a subscribed device.
                body.GetProperty("ReasonCode").GetString().ShouldBe("NoMatchingSubscribers");
                return body;
            }));
            var messages = new List<JsonElement>();
            for (var index = 0; index < replies.Length; index++)
            {
                var publication = await publications.ReceiveAsync(Deadline);
                var reply = replies.Single(item => item.GetProperty("Topic").GetString() == publication.Topic);
                var body = publication.Body;
                body.GetProperty("Command").GetString().ShouldBe("Move");
                publication.Topic.ShouldBe($"devices/motor-{body.GetProperty("Arguments").GetProperty("Position").GetInt32()}/commands");
                body.GetProperty("TraceId").GetString().ShouldBe(reply.GetProperty("TraceId").GetString());
                body.GetProperty("MessageId").GetString().ShouldNotBeNullOrWhiteSpace();
                messages.Add(body);
            }
            messages.Select(item => item.GetProperty("MessageId").GetString()).Distinct().Count().ShouldBe(6);
            messages.Select(item => item.GetProperty("TraceId").GetString()).Distinct().Count().ShouldBe(6);
            trigger.PendingCount.ShouldBe(0);
        }
        finally { await app.StopAsync().WaitAsync(Deadline); }
        publications.Count.ShouldBe(0);
        await AssertCommandsStopped(app, broker, trigger);
    }

    [Theory]
    [InlineData("malformed-json", 400)]
    [InlineData("invalid-command", 500)]
    [InlineData("offline", 500)]
    [InlineData("rejected", 500)]
    public async Task Http_commands_fail_without_publication_and_recover(string failure, int status)
    {
        await using var broker = await Broker.Start();
        var publications = new BufferBlock<string>();
        broker.Server.InterceptingPublishAsync += args =>
        {
            if (failure == "rejected" && args.ApplicationMessage.Topic == "devices/failed/commands")
            {
                args.ProcessPublish = false;
                args.Response.ReasonCode = MqttPubAckReasonCode.NotAuthorized;
            }
            else publications.Post(args.ApplicationMessage.Topic);
            return Task.CompletedTask;
        };
        await using var app = CreateCommandHost(broker);
        var trigger = CommandTrigger(app);
        var events = Capture(((MqttConnection)app.Services.GetWorkflowServices("commands").GetRequiredKeyedService<IFlowNode>("commands.connection")).Events);
        var publishResults = Capture(((MqttPublisher)app.Services.GetWorkflowServices("commands").GetRequiredKeyedService<IFlowNode>("commands.publish")).Output);
        try
        {
            await app.StartAsync().WaitAsync(Deadline);
            await WaitForEvent(events, "mqtt.connected");
            if (failure == "offline")
            {
                await broker.Server.StopAsync();
                await WaitForEvent(events, "mqtt.disconnected");
            }
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = Deadline };
            using var content = new StringContent(failure switch
            {
                "malformed-json" => "{",
                "invalid-command" => "{\"Command\":42}",
                _ => "{\"Command\":\"Move\"}"
            }, Encoding.UTF8, "application/json");
            using var failed = await client.PostAsync("/devices/failed/commands", content);
            ((int)failed.StatusCode).ShouldBe(status);
            (await failed.Content.ReadAsStringAsync()).ShouldBeEmpty();
            trigger.PendingCount.ShouldBe(0);
            publications.Count.ShouldBe(0);
            if (failure == "rejected")
            {
                var result = await publishResults.ReceiveAsync(Deadline);
                result.IsError.ShouldBeTrue();
                result.Error!.Message.ShouldContain("NotAuthorized");
                app.Services.GetRequiredFlowResource<MqttClientResource>("broker").IsConnected.ShouldBeTrue();
            }
            if (failure == "offline")
            {
                await broker.Server.StartAsync();
                await WaitForEvent(events, "mqtt.connected");
            }
            using var recovered = await client.PostAsJsonAsync("/devices/healthy/commands", new { Command = "Stop" }, JsonSerializerOptions.Default);
            recovered.StatusCode.ShouldBe(HttpStatusCode.OK);
            var reply = await recovered.Content.ReadFromJsonAsync<JsonElement>();
            reply.GetProperty("Topic").GetString().ShouldBe("devices/healthy/commands");
            (await publications.ReceiveAsync(Deadline)).ShouldBe("devices/healthy/commands");
            trigger.PendingCount.ShouldBe(0);
        }
        finally { await app.StopAsync().WaitAsync(Deadline); }
        publications.Count.ShouldBe(0);
        await AssertCommandsStopped(app, broker, trigger);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_commands_release_pending_replies_on_timeout_or_shutdown(bool shutdown)
    {
        await using var broker = await Broker.Start();
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        broker.Server.ClientDisconnectedAsync += args =>
        {
            if (args.ClientId == broker.ClientId) disconnected.TrySetResult();
            return Task.CompletedTask;
        };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        broker.Server.InterceptingPublishAsync += async args =>
        {
            if (args.ApplicationMessage.Topic == "devices/held/commands")
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(Deadline);
            }
        };
        await using var app = CreateCommandHost(broker, shortReplyDeadline: !shutdown);
        var trigger = CommandTrigger(app);
        var events = Capture(((MqttConnection)app.Services.GetWorkflowServices("commands").GetRequiredKeyedService<IFlowNode>("commands.connection")).Events);
        var requests = Capture(trigger.Output);
        Task? stopping = null;
        try
        {
            await app.StartAsync().WaitAsync(Deadline);
            await WaitForEvent(events, "mqtt.connected");
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = Deadline };
            var pending = client.PostAsJsonAsync("/devices/held/commands", new { Command = "Move" }, JsonSerializerOptions.Default);
            await entered.Task.WaitAsync(Deadline);
            await requests.ReceiveAsync(Deadline);
            if (shutdown) stopping = app.StopAsync();
            using var response = await pending;
            response.StatusCode.ShouldBe(shutdown ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.GatewayTimeout);
            (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
            trigger.PendingCount.ShouldBe(0);
            if (!shutdown)
            {
                // Admit the new exchange before releasing the old acknowledgement.
                var nextRequest = client.PostAsJsonAsync("/devices/next/commands", new { Command = "Stop" }, JsonSerializerOptions.Default);
                var request = await requests.ReceiveAsync(Deadline);
                request.Value.Json.GetProperty("RouteValues").GetProperty("id").GetString().ShouldBe("next");
                trigger.PendingCount.ShouldBe(1);
                release.TrySetResult();
                using var next = await nextRequest;
                next.StatusCode.ShouldBe(HttpStatusCode.OK);
                var body = await next.Content.ReadFromJsonAsync<JsonElement>();
                body.GetProperty("Topic").GetString().ShouldBe("devices/next/commands");
                trigger.PendingCount.ShouldBe(0);
            }
        }
        finally
        {
            release.TrySetResult();
            await (stopping ?? app.StopAsync()).WaitAsync(Deadline);
        }
        await disconnected.Task.WaitAsync(Deadline);
        await AssertCommandsStopped(app, broker, trigger);
    }

    private static HttpTrigger CommandTrigger(WebApplication app)
        => (HttpTrigger)app.Services.GetWorkflowServices("commands").GetRequiredKeyedService<IFlowNode>("commands.receive");

    private static async Task AssertCommandsStopped(WebApplication app, Broker broker, HttpTrigger trigger)
    {
        trigger.PendingCount.ShouldBe(0);
        app.Services.GetRequiredKeyedService<FlowWorkflow>("commands").Completion.IsCompletedSuccessfully.ShouldBeTrue();
        app.Services.GetRequiredFlowResource<MqttClientResource>("broker").IsConnected.ShouldBeFalse();
        (await broker.Server.GetClientsAsync()).ShouldNotContain(client => client.Id == broker.ClientId);
    }

    private static WebApplication CreateCommandHost(Broker broker, bool shortReplyDeadline = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "HttpSample.json"))
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "HttpCommands.json"));
        builder.Configuration["FluxFlow:Resources:broker:Address"] = $"mqtt://127.0.0.1:{broker.Port}";
        builder.Configuration["FluxFlow:Resources:broker:ClientId"] = broker.ClientId;
        if (shortReplyDeadline)
            builder.Configuration["FluxFlow:Workflows:commands:receive:ReplyTimeout"] = "00:00:00.5";
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpTriggers().AddHttp().AddMapper().AddMqtt().AddJsonataExpressionEngine()
            .AddFluxFlowApplication(builder.Configuration.GetRequiredSection("FluxFlow")).AddFluxFlow();
        var app = builder.Build();
        app.MapFluxFlowHttpTriggers();
        return app;
    }
}
