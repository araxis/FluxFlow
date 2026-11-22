using System.Net;
using System.Text.Json;
using FluxFlow.Execution;
using FluxFlow.Plugins.Http;
using FluxFlow.Execution.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class HttpTriggerRunTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_downstream_reply_cannot_complete_the_next_request_and_Stop_drains_a_successful_reply(bool disconnect)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        var lateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCurrent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drainEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDrain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var downstream = builder.Build();
        downstream.MapPost("/call", async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            if (body == "late")
            {
                lateEntered.TrySetResult();
                await releaseLate.Task.WaitAsync(context.RequestAborted);
            }
            if (body == "current") await releaseCurrent.Task.WaitAsync(context.RequestAborted);
            if (body == "drain")
            {
                drainEntered.TrySetResult();
                await releaseDrain.Task.WaitAsync(context.RequestAborted);
            }
            await context.Response.WriteAsync(body, context.RequestAborted);
        });
        await downstream.StartAsync(token);
        using var logging = ExecutionLogTests.CreateFactory();
        using var plugins = RunTestPlugins.WithBasicComponents(new HttpPlugin());
        var preparation = new EnvironmentPreparation([new HttpEnvironmentAdapter()], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        var request = new EnvironmentRunRequest("""
            {"Resources":{"api":{"Type":"http.client","BaseAddress":"ENDPOINT","Retry":{"MaxRetryAttempts":0}}},
             "Workflows":{"main":{
               "incoming":{"Type":"http.trigger","Route":"/call","ResponseMode":"Reply","ShutdownMode":"Drain",
                 "ReplyTimeout":"TIMEOUT","MaxPendingRequests":1,"Output":"request.Input"},
               "request":{"Type":"mapper","Engine":"jsonata","Expression":"{\"Method\":\"POST\",\"RequestUrl\":\"/call\",\"Body\":Body}","Output":"send.Input"},
               "send":{"Type":"http.client","Client":"api","Output":"reply.Input"},
               "reply":{"Type":"mapper","Engine":"jsonata","Expression":"{\"StatusCode\":StatusCode,\"Body\":Body}","Output":"incoming.Response"}
             }}}
            """.Replace("ENDPOINT", downstream.Urls.Single()).Replace("TIMEOUT", disconnect ? "00:01:00" : "00:00:02"), new());
        var original = JsonSerializer.Serialize(request);
        var id = Guid.NewGuid();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            var status = await runner.StartAsync(id, request, token);
            status.State.ShouldBe("Running", status.Error);
            var url = ListenerAddresses(status).ShouldHaveSingleItem();
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
            var first = client.PostAsync(url + "/call", new StringContent("late"), caller.Token);
            await lateEntered.Task.WaitAsync(token);
            using var refused = await client.PostAsync(url + "/call", new StringContent("overflow"), token);
            refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            if (disconnect)
            {
                caller.Cancel();
                await Should.ThrowAsync<OperationCanceledException>(async () => { using var response = await first; });
            }
            else
            {
                using var late = await first;
                late.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
            }

            async Task<HttpResponseMessage> PostCurrentAsync()
            {
                while (true)
                {
                    using var body = new StringContent("current");
                    var response = await client.PostAsync(url + "/call", body, token);
                    // The client can observe cancellation before Kestrel observes the disconnect.
                    if (!disconnect || response.StatusCode != HttpStatusCode.ServiceUnavailable) return response;
                    response.Dispose();
                    await Task.Delay(10, token);
                }
            }
            var next = PostCurrentAsync();
            while ((await runner.GetAsync(id, token)).Outputs!.Records.Count(record => record.Port == "main.incoming.Output") < 2)
                await Task.Delay(10, token);
            releaseLate.TrySetResult();
            while ((await runner.GetAsync(id, token)).Outputs!.Records.All(record => record.Port != "main.reply.Output"))
                await Task.Delay(10, token);
            releaseCurrent.TrySetResult();
            using var current = await next;
            current.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await current.Content.ReadAsStringAsync(token)).ShouldBe("current");

            var draining = client.PostAsync(url + "/call", new StringContent("drain"), token);
            await drainEntered.Task.WaitAsync(token);
            var stop = runner.StopAsync(id, token);
            (await runner.GetAsync(id, token)).State.ShouldBe("Stopping");
            stop.IsCompleted.ShouldBeFalse();
            releaseDrain.TrySetResult();
            using var completed = await draining;
            completed.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await completed.Content.ReadAsStringAsync(token)).ShouldBe("drain");
            await stop;
            var stopped = await runner.GetAsync(id, token);
            stopped.State.ShouldBe("Stopped", stopped.Error);
            JsonElement[] Messages(string port) => stopped.Outputs!.Records.Where(r => r.Port == port).Select(r => r.Message!.Value).ToArray();
            var incoming = Messages("main.incoming.Output");
            incoming.Select(m => m.GetProperty("Value").GetProperty("Body").GetString()).ShouldBe(new[] { "late", "current", "drain" });
            var ids = incoming.Select(m => m.GetProperty("CorrelationId").GetString()!).ToArray();
            ids.Distinct().Count().ShouldBe(3);
            Messages("main.reply.Output").Select(m => m.GetProperty("CorrelationId").GetString()).ShouldBe(ids);
            Messages("main.incoming.Events").Where(m => m.GetProperty("Value").GetProperty("Name").GetString() == "http.reply_received")
                .Select(m => m.GetProperty("CorrelationId").GetString()).ShouldBe(ids.Skip(1));

            var rerun = Guid.NewGuid();
            try
            {
                var restarted = await runner.StartAsync(rerun, request, token);
                restarted.State.ShouldBe("Running", restarted.Error);
                using var fresh = await client.PostAsync(ListenerAddresses(restarted).ShouldHaveSingleItem() + "/call", new StringContent("fresh"), token);
                fresh.StatusCode.ShouldBe(HttpStatusCode.OK);
                (await fresh.Content.ReadAsStringAsync(token)).ShouldBe("fresh");
                await runner.StopAsync(rerun, token);
                var finished = await runner.GetAsync(rerun, token);
                finished.State.ShouldBe("Stopped", finished.Error);
                var published = finished.Outputs!.Records.Single(r => r.Port == "main.incoming.Output").Message!.Value;
                ids.ShouldNotContain(published.GetProperty("CorrelationId").GetString()!);
            }
            finally { await runner.StopAsync(rerun, CancellationToken.None); }
            JsonSerializer.Serialize(request).ShouldBe(original);
        }
        finally
        {
            releaseLate.TrySetResult();
            releaseCurrent.TrySetResult();
            releaseDrain.TrySetResult();
            await runner.StopAsync(id, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Failed_second_instance_closes_both_listeners_and_allows_rerun()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        using var logging = ExecutionLogTests.CreateFactory();
        using var plugins = RunTestPlugins.WithBasicComponents(new HttpPlugin());
        var preparation = new EnvironmentPreparation([], []);
        var failure = new InvalidOperationException("Second HTTP instance could not start.");
        var support = new FailingSecondStart(failure);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, hostSupport: [support], plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        var request = new EnvironmentRunRequest("""
            {"Workflows":{"main":{"incoming":{"Type":"http.trigger","Route":"/accepted"}}}}
            """, new()) { Instances = [new("first", "First"), new("second", "Second")] };
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var id = Guid.NewGuid();
            try
            {
                var result = await runner.StartAsync(id, request, token);
                result.State.ShouldBe(attempt == 0 ? "Failed" : "Running", result.Error);
                var urls = ListenerAddresses(result);
                urls.Length.ShouldBe(2);
                if (attempt == 0) result.Error.ShouldNotBeNull().ShouldContain(failure.Message);
                else
                    foreach (var url in urls)
                    {
                        using var response = await client.PostAsync(url + "/accepted", new StringContent("rerun"), token);
                        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
                    }
                await runner.StopAsync(id, token);
                foreach (var url in urls)
                    await Should.ThrowAsync<HttpRequestException>(() => client.PostAsync(url + "/accepted", new StringContent("stopped"), token));
            }
            finally { await runner.StopAsync(id, CancellationToken.None); }
        }
    }

    private sealed class FailingSecondStart(Exception failure) : IWorkflowHostSupport
    {
        private int _starts;
        public void ConfigureInstance(IServiceCollection services) => services.AddSingleton<IHostedService>(new Startup(this, failure));
        public Task CheckAsync(IConfiguration configuration, CancellationToken cancellationToken) => Task.CompletedTask;
        private sealed class Startup(FailingSecondStart owner, Exception failure) : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken) => Interlocked.Increment(ref owner._starts) == 2
                ? Task.FromException(failure) : Task.CompletedTask;
            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Normal_run_maps_routes_correlates_replies_and_keeps_instances_and_reruns_isolated()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = deadline.Token;
        using var logging = ExecutionLogTests.CreateFactory();
        using var plugins = RunTestPlugins.WithBasicComponents(new HttpPlugin());
        var preparation = new EnvironmentPreparation([], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        var request = new EnvironmentRunRequest("""
            {"Workflows":{"main":{
              "incoming":{"Type":"http.trigger","Route":"/echo","ResponseMode":"Reply","Output":"reply.Input"},
              "reply":{"Type":"mapper","Engine":"jsonata","Expression":"{\"StatusCode\":200,\"Body\":Body}","Output":"incoming.Response"},
              "accepted":{"Type":"http.trigger","Route":"/accepted"},
              "timeout":{"Type":"http.trigger","Route":"/timeout","ResponseMode":"Reply","ReplyTimeout":"00:00:00.100"}
            }}}
            """, new()) { Instances = [new("first", "First"), new("second", "Second")] };
        var original = JsonSerializer.Serialize(request);
        (await runner.CheckAsync(request, token)).Success.ShouldBeTrue();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        string? retained = null;
        var previous = Guid.Empty;
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var id = Guid.NewGuid();
            try
            {
                var running = await runner.StartAsync(id, request, token);
                running.State.ShouldBe("Running", running.Error);
                var urls = ListenerAddresses(running);
                urls.Length.ShouldBe(2);
                urls.Distinct().Count().ShouldBe(2);
                foreach (var url in urls)
                {
                    await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
                    {
                        var payload = $"run-{iteration}/message-{index}/café";
                        using var response = await client.PostAsync(url + "/echo", new StringContent(payload), token);
                        response.StatusCode.ShouldBe(HttpStatusCode.OK);
                        (await response.Content.ReadAsStringAsync(token)).ShouldBe(payload);
                    }));
                    using var accepted = await client.PostAsync(url + "/accepted", new StringContent("accepted"), token);
                    accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
                    using var timeout = await client.PostAsync(url + "/timeout", new StringContent("timeout"), token);
                    timeout.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
                }
                await runner.StopAsync(id, token);
                var stopped = await runner.GetAsync(id, token);
                stopped.State.ShouldBe("Stopped", stopped.Error);
                var requests = stopped.Outputs!.Records.Where(record => record.Port == "main.incoming.Output").ToArray();
                requests.Length.ShouldBe(16);
                requests.Select(record => record.Message!.Value.GetProperty("CorrelationId").GetString()).Distinct().Count().ShouldBe(16);
                requests.Select(record => record.InstanceId).Distinct().Order().ShouldBe(new[] { "first", "second" });
                foreach (var url in urls)
                    await Should.ThrowAsync<HttpRequestException>(() => client.PostAsync(url + "/echo", new StringContent("stopped"), token));
                if (retained != null)
                    JsonSerializer.Serialize((await runner.GetAsync(previous, token)).Outputs).ShouldBe(retained);
                retained = JsonSerializer.Serialize(stopped.Outputs);
                previous = id;
            }
            finally { await runner.StopAsync(id, CancellationToken.None); }
        }
        JsonSerializer.Serialize(request).ShouldBe(original);
    }

    [Theory]
    [InlineData("Cancel", HttpStatusCode.ServiceUnavailable)]
    [InlineData("Drain", HttpStatusCode.GatewayTimeout)]
    public async Task Stop_respects_pending_reply_mode_and_closes_the_listener(string mode, HttpStatusCode expected)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        using var logging = ExecutionLogTests.CreateFactory();
        using var plugins = RunTestPlugins.WithBasicComponents(new HttpPlugin());
        var preparation = new EnvironmentPreparation([], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        var request = new EnvironmentRunRequest("""
            {"Workflows":{"main":{"incoming":{"Type":"http.trigger","Route":"/pending",
              "ResponseMode":"Reply","ShutdownMode":"MODE","ReplyTimeout":"00:00:02"}}}}
            """.Replace("MODE", mode), new());
        var id = Guid.NewGuid();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            var status = await runner.StartAsync(id, request, token);
            status.State.ShouldBe("Running", status.Error);
            var url = ListenerAddresses(status).ShouldHaveSingleItem();
            var pending = client.PostAsync(url + "/pending", new StringContent("waiting"), token);
            while ((await runner.GetAsync(id, token)).Outputs!.Records.All(record => record.Port != "main.incoming.Output"))
                await Task.Delay(10, token);
            await runner.StopAsync(id, token);
            using var response = await pending;
            response.StatusCode.ShouldBe(expected);
            (await runner.GetAsync(id, token)).State.ShouldBe("Stopped");
            await Should.ThrowAsync<HttpRequestException>(() => client.PostAsync(url + "/pending", new StringContent("stopped"), token));
        }
        finally { await runner.StopAsync(id, CancellationToken.None); }
    }

    internal static string[] ListenerAddresses(EnvironmentRunStatus status) => status.Logs!.Records
        .Where(record => record.Category == "Microsoft.Hosting.Lifetime" && record.Message.StartsWith("Now listening on: ", StringComparison.Ordinal))
        .Select(record => record.Message["Now listening on: ".Length..]).ToArray();
}
