using System.Net;
using System.Text;
using System.Text.Json;
using FluxFlow.Application;
using FluxFlow.Components.Http;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Http;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Core.Nodes;
using FluxFlow.Expressions.Jsonata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Hosting.AspNetCore.Tests;

public sealed class HttpSampleTests
{
    [Fact]
    public async Task Workflow_restart_reuses_host_routes_and_configuration_with_fresh_reply_components()
    {
        await using var app = CreateSample();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(10) };
        var workflow = app.Services.GetRequiredKeyedService<FlowWorkflow>("api");
        var first = (HttpTrigger)workflow.Services.GetRequiredKeyedService<IFlowNode>("api.receive");
        first.ShutdownMode.ShouldBe(HttpTriggerShutdownMode.Drain);
        var uploads = app.Services.GetWorkflowServices("uploads").GetRequiredKeyedService<IFlowNode>("uploads.receive");
        try
        {
            await AssertReply();
            await workflow.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
            first.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            first.PendingCount.ShouldBe(0);
            uploads.Completion.IsCompleted.ShouldBeFalse();
            using var unavailable = await client.PostAsync("/devices/sensor-7/readings", new StringContent("{}"));
            unavailable.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            await workflow.StartAsync();
            var restarted = (HttpTrigger)workflow.Services.GetRequiredKeyedService<IFlowNode>("api.receive");
            restarted.ShouldNotBeSameAs(first);
            restarted.ShutdownMode.ShouldBe(HttpTriggerShutdownMode.Drain);
            await AssertReply();
            restarted.PendingCount.ShouldBe(0);
            app.Services.GetWorkflowServices("uploads").GetRequiredKeyedService<IFlowNode>("uploads.receive").ShouldBeSameAs(uploads);
        }
        finally { await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        workflow.Completion.IsCompletedSuccessfully.ShouldBeTrue();

        async Task AssertReply()
        {
            using var content = new StringContent("{\"value\":23,\"unit\":\"C\"}", Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("/devices/sensor-7/readings", content);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("reading").GetInt32().ShouldBe(23);
            json.RootElement.GetProperty("messageId").GetString().ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task Sample_configuration_maps_real_requests_to_correlated_jsonata_replies_and_stops()
    {
        await using var app = CreateSample();
        await app.StartAsync();
        var trigger = (HttpTrigger)app.Services.GetWorkflowServices("api").GetRequiredKeyedService<IFlowNode>("api.receive");
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(10) };
            var replies = await Task.WhenAll(Enumerable.Range(1, 8).Select(async index =>
            {
                using var content = new StringContent($$"""{"value":{{index}},"unit":"C"}""", Encoding.UTF8, "application/json");
                using var response = await client.PostAsync($"/devices/sensor-{index}/readings", content);
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                response.Headers.GetValues("X-Device-Id").Single().ShouldBe($"sensor-{index}");
                response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var body = json.RootElement;
                body.GetProperty("deviceId").GetString().ShouldBe($"sensor-{index}");
                body.GetProperty("reading").GetInt32().ShouldBe(index);
                body.GetProperty("unit").GetString().ShouldBe("C");
                body.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
                return body.GetProperty("messageId").GetString();
            }));
            replies.ShouldAllBe(id => !string.IsNullOrWhiteSpace(id));
            replies.Distinct().Count().ShouldBe(8);

            using var malformed = new StringContent("{", Encoding.UTF8, "application/json");
            using var invalid = await client.PostAsync("/devices/sensor-7/readings", malformed);
            invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            using var wrongMethod = await client.GetAsync("/devices/sensor-7/readings");
            wrongMethod.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
            using var missing = await client.GetAsync("/missing");
            missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        trigger.PendingCount.ShouldBe(0);
        trigger.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        app.Services.GetRequiredKeyedService<FlowWorkflow>("api").Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Upload_configuration_returns_each_requests_fields_and_file_summary(int fileCount)
    {
        await using var app = CreateSample();
        await app.StartAsync();
        var trigger = (HttpTrigger)app.Services.GetWorkflowServices("uploads").GetRequiredKeyedService<IFlowNode>("uploads.receive");
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(10) };
            var replies = await Task.WhenAll(Enumerable.Range(1, 4).Select(async index =>
            {
                using var content = new MultipartFormDataContent();
                content.Add(new StringContent($"sensor-{index}"), "device");
                content.Add(new StringContent("robotics"), "tag");
                content.Add(new StringContent("bench"), "tag");
                for (var file = 0; file < fileCount; file++)
                {
                    var bytes = new ByteArrayContent(file == 0 ? new byte[] { 0, 255, (byte)index } : []);
                    bytes.Headers.ContentType = new("application/octet-stream");
                    content.Add(bytes, "upload", $"sensor-{index}-{file}.bin");
                }
                using var response = await client.PostAsync("/uploads", content);
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var body = json.RootElement;
                var fields = body.GetProperty("fields");
                fields.GetProperty("device")[0].GetString().ShouldBe($"sensor-{index}");
                fields.GetProperty("tag").EnumerateArray().Select(value => value.GetString()).ShouldBe(["robotics", "bench"]);
                var files = body.GetProperty("files");
                files.ValueKind.ShouldBe(JsonValueKind.Array);
                files.GetArrayLength().ShouldBe(fileCount);
                for (var file = 0; file < fileCount; file++)
                {
                    files[file].GetProperty("name").GetString().ShouldBe("upload");
                    files[file].GetProperty("fileName").GetString().ShouldBe($"sensor-{index}-{file}.bin");
                    files[file].GetProperty("contentType").GetString().ShouldBe("application/octet-stream");
                    files[file].GetProperty("length").GetInt64().ShouldBe(file == 0 ? 3 : 0);
                    files[file].TryGetProperty("content", out _).ShouldBeFalse();
                }
                body.TryGetProperty("body", out _).ShouldBeFalse();
                body.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
                return body.GetProperty("messageId").GetString();
            }));
            replies.ShouldAllBe(id => !string.IsNullOrWhiteSpace(id));
            replies.Distinct().Count().ShouldBe(4);
        }
        finally
        {
            await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        trigger.PendingCount.ShouldBe(0);
        trigger.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        app.Services.GetRequiredKeyedService<FlowWorkflow>("uploads").Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    private static WebApplication CreateSample()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "HttpSample.json"));
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpTriggers().AddHttp().AddMapper().AddJsonataExpressionEngine()
            .AddFluxFlowApplication(builder.Configuration.GetRequiredSection("FluxFlow")).AddFluxFlow();
        var app = builder.Build();
        app.MapFluxFlowHttpTriggers();
        return app;
    }
}
