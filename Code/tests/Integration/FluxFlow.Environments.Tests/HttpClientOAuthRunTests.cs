using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Execution;
using FluxFlow.Plugins.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace FluxFlow.Environments.Tests;

public sealed class HttpClientOAuthRunTests(ITestOutputHelper report)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task One_deadline_during_shared_token_acquisition_preserves_the_other_post_and_rerun(bool shortFirst)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = deadline.Token;
        var tokenEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseToken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var apiCalls = new ConcurrentQueue<(string Path, string Authorization)>();
        var tokenCalls = 0;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        server.MapPost("/token", async (HttpContext context) =>
        {
            var number = Interlocked.Increment(ref tokenCalls);
            tokenEntered.TrySetResult();
            await releaseToken.Task.WaitAsync(context.RequestAborted);
            await context.Response.WriteAsJsonAsync(new { access_token = $"trial-{number}", token_type = "Bearer", expires_in = 3600 });
        });
        server.MapPost("/api/{name}", (HttpContext context, string name) =>
        {
            apiCalls.Enqueue((name, context.Request.Headers.Authorization.ToString()));
            return Results.Text(name);
        });
        await server.StartAsync(token);
        using var config = WorkflowExecution.ReadConfiguration(JsonSerializer.Serialize(new
        {
            Resources = new { api = new { Type = "http.client", BaseAddress = server.Urls.Single(),
                Authentication = new { Scheme = "OAuthClientCredentials", TokenEndpoint = server.Urls.Single() + "/token",
                    ClientId = "trial", ClientSecret = "trial-secret" }, Retry = new { MaxRetryAttempts = 0 } } },
            Workflows = new { main = new { brief = new { Type = "http.client", Client = "api" },
                patient = new { Type = "http.client", Client = "api" } } }
        }));
        using var plugins = RunTestPlugins.WithBasicComponents(new HttpPlugin());
        for (var run = 0; run < 2; run++)
        {
            var services = new ServiceCollection();
            WorkflowExecution.Register(services, config, plugins);
            await using var provider = services.BuildServiceProvider();
            await using var app = await provider.BuildApplicationAsync();
            await app.StartAsync(token);
            var ports = provider.GetWorkflowServices("main");
            ITargetBlock<FlowMessage> Input(string name) => ports.GetRequiredKeyedService<ITargetBlock<FlowMessage>>($"main.{name}.Input");
            BufferBlock<FlowMessage> Capture(string name)
            {
                var sink = new BufferBlock<FlowMessage>();
                ports.GetRequiredKeyedService<ISourceBlock<FlowMessage>>($"main.{name}.Output").LinkTo(sink, new() { PropagateCompletion = true });
                return sink;
            }
            var shortOutput = Capture("brief");
            var longOutput = Capture("patient");
            var path = run == 0 ? "survivor" : "rerun";
            var patient = Request(path, "00:00:10");
            var previousTokenCalls = Volatile.Read(ref tokenCalls);
            if (run == 0)
            {
                var brief = Request("expired", "00:00:01");
                using var secondStarted = new RequestStartObserver(new Uri(server.Urls.Single()).Port,
                    shortFirst ? "/api/survivor" : "/api/expired");
                (await Input(shortFirst ? "brief" : "patient").SendAsync(shortFirst ? brief : patient, token)).ShouldBeTrue();
                await tokenEntered.Task.WaitAsync(token);
                (await Input(shortFirst ? "patient" : "brief").SendAsync(shortFirst ? patient : brief, token)).ShouldBeTrue();
                await secondStarted.Started.Task.WaitAsync(token);
                shortOutput.Count.ShouldBe(0);
                var failure = await shortOutput.ReceiveAsync(token);
                failure.Value.ShouldBeOfType<FlowError>().Details!.Json.GetProperty("exceptionType").GetString()
                    .ShouldBeOneOf(typeof(TaskCanceledException).FullName, typeof(OperationCanceledException).FullName);
                failure.CorrelationId.ShouldBe(brief.CorrelationId);
                failure.CausationId.ShouldBe(brief.MessageId);
                longOutput.Count.ShouldBe(0);
                apiCalls.ShouldBeEmpty();
            }
            else (await Input("patient").SendAsync(patient, token)).ShouldBeTrue();

            var stop = app.StopAsync(token);
            if (run == 0) stop.IsCompleted.ShouldBeFalse();
            releaseToken.TrySetResult();
            var success = await longOutput.ReceiveAsync(token);
            success.IsError.ShouldBeFalse(success.Error?.Message);
            success.CorrelationId.ShouldBe(patient.CorrelationId);
            success.CausationId.ShouldBe(patient.MessageId);
            success.Value.Json.GetProperty("StatusCode").GetInt32().ShouldBe(200);
            success.Value.Json.GetProperty("Body").GetString().ShouldBe(path);
            await stop.WaitAsync(token);
            var issued = Volatile.Read(ref tokenCalls);
            issued.ShouldBeGreaterThan(previousTokenCalls);
            apiCalls.ToArray().ShouldHaveSingleItem().ShouldBe((path, $"Bearer trial-{issued}"));
            report.WriteLine($"Run {run + 1}: {issued - previousTokenCalls} token request(s), {apiCalls.Count} API POST.");
            apiCalls.Clear();
            (await shortOutput.OutputAvailableAsync(token)).ShouldBeFalse();
            (await longOutput.OutputAvailableAsync(token)).ShouldBeFalse();
        }
    }

    private static FlowMessage Request(string name, string timeout) => FlowMessage.Create(
        new JsonFlowValue(JsonSerializer.SerializeToElement(new { Method = "POST", RequestUrl = "/api/" + name, Timeout = timeout })),
        CorrelationId.New());

    // HttpClient emits RequestStart before its OAuth handler runs, even for a POST that never reaches the API.
    private sealed class RequestStartObserver(int port, string path) : EventListener
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "System.Net.Http") EnableEvents(source, EventLevel.Informational);
        }

        protected override void OnEventWritten(EventWrittenEventArgs data)
        {
            if (data.EventName == "RequestStart" && data.Payload is [_, _, int requestPort, string requestPath, ..]
                && requestPort == port && requestPath == path)
                Started.TrySetResult();
        }
    }
}
