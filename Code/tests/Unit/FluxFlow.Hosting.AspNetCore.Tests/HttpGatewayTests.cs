using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Components.Http;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Http;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Core.Nodes;
using FluxFlow.Expressions.Jsonata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Hosting.AspNetCore.Tests;

public sealed class HttpGatewayTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Workflow_restart_preserves_shared_HTTP_resource_and_other_gateway_delivery()
    {
        await using var downstream = CreateServer();
        downstream.MapPost("/devices/{id}/readings", (string id) => Results.Text(id));
        await downstream.StartAsync();
        try
        {
            HttpClient shared;
            await using (var gateway = CreateGateway(downstream.Urls.Single(), secondWorkflow: true))
            {
                await gateway.StartAsync();
                shared = gateway.Services.GetRequiredFlowResource<HttpClient>("downstream");
                var first = gateway.Services.GetRequiredKeyedService<FlowWorkflow>("gateway");
                var second = gateway.Services.GetRequiredKeyedService<FlowWorkflow>("second");
                var original = first.Services.GetRequiredKeyedService<IFlowNode>("gateway.send");
                var continuing = second.Services.GetRequiredKeyedService<IFlowNode>("second.send");
                var firstTrigger = Trigger(gateway);
                var activeTrigger = firstTrigger;
                var secondTrigger = (HttpTrigger)second.Services.GetRequiredKeyedService<IFlowNode>("second.receive");
                using var client = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()), Timeout = Deadline };
                try
                {
                    first.Services.GetRequiredFlowResource<HttpClient>("downstream", "gateway").ShouldBeSameAs(shared);
                    second.Services.GetRequiredFlowResource<HttpClient>("downstream", "second").ShouldBeSameAs(shared);
                    await AssertReply("gateway", "before-stop");
                    await AssertReply("second", "before-stop");

                    await first.StopAsync().WaitAsync(Deadline);
                    first.Completion.IsCompletedSuccessfully.ShouldBeTrue();
                    original.Completion.IsCompletedSuccessfully.ShouldBeTrue();
                    firstTrigger.PendingCount.ShouldBe(0);
                    using var stopped = await client.PostAsJsonAsync("/gateway/devices/stopped/readings", new { value = 1 });
                    stopped.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
                    await AssertReply("second", "while-stopped");
                    continuing.Completion.IsCompleted.ShouldBeFalse();

                    await first.StartAsync().WaitAsync(Deadline);
                    first.Services.GetRequiredKeyedService<IFlowNode>("gateway.send").ShouldNotBeSameAs(original);
                    activeTrigger = Trigger(gateway);
                    activeTrigger.ShouldNotBeSameAs(firstTrigger);
                    first.Services.GetRequiredFlowResource<HttpClient>("downstream", "gateway").ShouldBeSameAs(shared);
                    second.Services.GetRequiredKeyedService<IFlowNode>("second.send").ShouldBeSameAs(continuing);
                    second.Services.GetRequiredFlowResource<HttpClient>("downstream", "second").ShouldBeSameAs(shared);
                    await AssertReply("gateway", "after-restart");
                    await AssertReply("second", "after-restart");
                }
                finally { await gateway.StopAsync().WaitAsync(Deadline); }
                first.Completion.IsCompletedSuccessfully.ShouldBeTrue();
                second.Completion.IsCompletedSuccessfully.ShouldBeTrue();
                activeTrigger.Completion.IsCompletedSuccessfully.ShouldBeTrue();
                activeTrigger.PendingCount.ShouldBe(0);
                secondTrigger.Completion.IsCompletedSuccessfully.ShouldBeTrue();
                secondTrigger.PendingCount.ShouldBe(0);
                using var response = await shared.PostAsJsonAsync("devices/after-stop/readings", new { value = 1 }).WaitAsync(Deadline);
                response.StatusCode.ShouldBe(HttpStatusCode.OK); // The provider owns disposal, not workflow stop.

                async Task AssertReply(string workflow, string id)
                {
                    using var response = await client.PostAsJsonAsync($"/{workflow}/devices/{id}/readings", new { value = 1 });
                    response.StatusCode.ShouldBe(HttpStatusCode.OK);
                    (await response.Content.ReadAsStringAsync()).ShouldBe(id);
                }
            }
            await Should.ThrowAsync<ObjectDisposedException>(() => shared.GetAsync("/"));
        }
        finally { await downstream.StopAsync().WaitAsync(Deadline); }
    }

    [Theory]
    [InlineData(201)]
    [InlineData(422)]
    [InlineData(503)]
    public async Task Configured_gateway_preserves_downstream_status_body_and_request_correlation(int status)
    {
        await using var downstream = CreateServer();
        downstream.MapPost("/devices/{id}/readings", async (HttpContext context) =>
        {
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            await Results.Json(new
            {
                routeId = (string)context.Request.RouteValues["id"]!,
                received = body.RootElement,
                messageId = context.Request.Headers["X-Flow-Message-Id"].ToString(),
                authorization = context.Request.Headers.Authorization.ToString(),
                cookie = context.Request.Headers.Cookie.ToString()
            }, statusCode: status).ExecuteAsync(context);
        });
        await downstream.StartAsync();
        try
        {
            await using var gateway = CreateGateway(downstream.Urls.Single());
            await gateway.StartAsync();
            var trigger = Trigger(gateway);
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()), Timeout = Deadline };
                client.DefaultRequestHeaders.Authorization = new("Bearer", "inbound-only");
                client.DefaultRequestHeaders.Add("Cookie", "session=inbound-only");
                var ids = await Task.WhenAll(Enumerable.Range(1, 6).Select(async index =>
                {
                    using var response = await client.PostAsJsonAsync($"/gateway/devices/sensor-{index}/readings", new { value = index, unit = "C" });
                    ((int)response.StatusCode).ShouldBe(status);
                    response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
                    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                    body.GetProperty("routeId").GetString().ShouldBe($"sensor-{index}");
                    var received = body.GetProperty("received");
                    received.GetProperty("deviceId").GetString().ShouldBe($"sensor-{index}");
                    received.GetProperty("value").GetInt32().ShouldBe(index);
                    received.GetProperty("unit").GetString().ShouldBe("C");
                    body.GetProperty("authorization").GetString().ShouldBeEmpty();
                    body.GetProperty("cookie").GetString().ShouldBeEmpty();
                    return body.GetProperty("messageId").GetString();
                }));
                ids.ShouldAllBe(id => !string.IsNullOrWhiteSpace(id));
                ids.Distinct().Count().ShouldBe(6);
                trigger.PendingCount.ShouldBe(0);
            }
            finally { await gateway.StopAsync().WaitAsync(Deadline); }
            trigger.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            gateway.Services.GetRequiredKeyedService<FlowWorkflow>("gateway").Completion.IsCompletedSuccessfully.ShouldBeTrue();
        }
        finally { await downstream.StopAsync().WaitAsync(Deadline); }
    }

    [Theory]
    [InlineData("disconnect", 500)]
    [InlineData("truncated-body", 500)]
    [InlineData("client-timeout", 500)]
    [InlineData("reply-timeout", 504)]
    public async Task Failed_or_timed_out_requests_release_waiters_and_allow_a_later_request(string failure, int status)
    {
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var downstream = CreateServer();
        downstream.MapPost("/devices/{id}/readings", async (HttpContext context) =>
        {
            if ((string)context.Request.RouteValues["id"]! == "healthy")
            {
                await Results.Text("recovered").ExecuteAsync(context);
                return;
            }
            if (failure != "disconnect")
            {
                context.Response.ContentLength = 100;
                await context.Response.WriteAsync("partial");
                await context.Response.Body.FlushAsync();
            }
            if (failure is "disconnect" or "truncated-body")
            {
                context.Abort();
                canceled.TrySetResult();
                return;
            }
            try { await Task.Delay(Timeout.Infinite, context.RequestAborted); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            { canceled.TrySetResult(); }
        });
        await downstream.StartAsync();
        try
        {
            await using var gateway = CreateGateway(downstream.Urls.Single(), failure == "reply-timeout");
            await gateway.StartAsync();
            var trigger = Trigger(gateway);
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()), Timeout = Deadline };
                using var response = await client.PostAsJsonAsync("/gateway/devices/failing/readings", new { value = 1 });
                ((int)response.StatusCode).ShouldBe(status);
                (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
                trigger.PendingCount.ShouldBe(0);
                await canceled.Task.WaitAsync(Deadline);

                using var next = await client.PostAsJsonAsync("/gateway/devices/healthy/readings", new { value = 2 });
                next.StatusCode.ShouldBe(HttpStatusCode.OK);
                next.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
                (await next.Content.ReadAsStringAsync()).ShouldBe("recovered");
                trigger.PendingCount.ShouldBe(0);
            }
            finally { await gateway.StopAsync().WaitAsync(Deadline); }
            trigger.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        }
        finally { await downstream.StopAsync().WaitAsync(Deadline); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_downstream_reply_after_stop_or_timeout_cannot_complete_a_rerun_request(bool replyTimesOut)
    {
        var oldEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var downstream = CreateServer();
        downstream.MapPost("/devices/{id}/readings", async (string id, CancellationToken aborted) =>
        {
            var (entered, release) = id == "old" ? (oldEntered, oldRelease) : (nextEntered, nextRelease);
            entered.SetResult();
            await release.Task.WaitAsync(Deadline, aborted);
            return Results.Text(id);
        });
        await downstream.StartAsync();
        await using var gateway = CreateGateway(downstream.Urls.Single(), shortReplyDeadline: replyTimesOut);
        await gateway.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()), Timeout = Deadline };
            var workflow = gateway.Services.GetRequiredKeyedService<FlowWorkflow>("gateway");
            var original = Trigger(gateway);
            using var replies = gateway.Services.ObserveOutput(PortAddress.Parse("gateway.reply.Output"));
            var pending = client.PostAsJsonAsync("/gateway/devices/old/readings", new { value = 1 });
            await oldEntered.Task.WaitAsync(Deadline);
            var stopping = replyTimesOut ? null : workflow.StopAsync();
            using var expired = await pending.WaitAsync(Deadline);
            expired.StatusCode.ShouldBe(replyTimesOut ? HttpStatusCode.GatewayTimeout : HttpStatusCode.ServiceUnavailable);
            original.PendingCount.ShouldBe(0);
            stopping ??= workflow.StopAsync();
            oldRelease.SetResult();
            var lateReply = await replies.Messages.ReceiveAsync(Deadline);
            lateReply.Value.Json.GetProperty("Body").GetString().ShouldBe("old");
            await stopping.WaitAsync(Deadline);

            await workflow.StartAsync().WaitAsync(Deadline);
            var restarted = Trigger(gateway);
            var next = client.PostAsJsonAsync("/gateway/devices/new/readings", new { value = 2 });
            await nextEntered.Task.WaitAsync(Deadline);
            restarted.PendingCount.ShouldBe(1);
            // Replay the actual mapped reply while the new caller is still waiting.
            (await restarted.Response.SendAsync(lateReply)).ShouldBeTrue();
            nextRelease.SetResult();
            using var recovered = await next.WaitAsync(Deadline);
            recovered.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await recovered.Content.ReadAsStringAsync()).ShouldBe("new");
            restarted.PendingCount.ShouldBe(0);
        }
        finally
        {
            oldRelease.TrySetResult();
            nextRelease.TrySetResult();
            await gateway.StopAsync().WaitAsync(Deadline);
            await downstream.StopAsync().WaitAsync(Deadline);
        }
    }

    private static HttpTrigger Trigger(WebApplication app)
        => (HttpTrigger)app.Services.GetWorkflowServices("gateway").GetRequiredKeyedService<IFlowNode>("gateway.receive");

    private static WebApplication CreateServer()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder.Build();
    }

    private static WebApplication CreateGateway(string downstreamAddress, bool shortReplyDeadline = false, bool secondWorkflow = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "HttpSample.json"));
        builder.Configuration["FluxFlow:Resources:downstream:BaseAddress"] = downstreamAddress + "/";
        if (shortReplyDeadline)
            builder.Configuration["FluxFlow:Workflows:gateway:receive:ReplyTimeout"] = "00:00:00.5";
        if (secondWorkflow)
        {
            var settings = builder.Configuration.GetRequiredSection("FluxFlow:Workflows:gateway")
                .AsEnumerable(makePathsRelative: true)
                .ToDictionary(item => "FluxFlow:Workflows:second:" + item.Key, item => item.Value);
            settings["FluxFlow:Workflows:second:receive:Route"] = "/second/devices/{id}/readings";
            builder.Configuration.AddInMemoryCollection(settings);
        }
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpTriggers().AddHttp().AddMapper().AddJsonataExpressionEngine()
            .AddFluxFlowApplication(builder.Configuration.GetRequiredSection("FluxFlow")).AddFluxFlow();
        var app = builder.Build();
        app.MapFluxFlowHttpTriggers();
        return app;
    }
}
