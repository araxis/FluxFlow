using System.Text.Json;
using FluxFlow.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class HttpClientRunTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_drains_an_in_flight_post_and_rerun_has_its_own_response(bool stallUntilTimeout)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        var partialBody = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishBody = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var posts = 0;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        server.MapPost("/submit", async (HttpContext http) =>
        {
            if (Interlocked.Increment(ref posts) == 1)
            {
                await http.Response.WriteAsync("first-");
                await http.Response.Body.FlushAsync();
                partialBody.SetResult();
                try { await finishBody.Task.WaitAsync(http.RequestAborted); }
                catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
                {
                    disconnected.TrySetResult();
                    return;
                }
                await http.Response.WriteAsync("run");
            }
            else await http.Response.WriteAsync("next-run");
        });
        await server.StartAsync(token);
        var request = new EnvironmentRunRequest($$$$"""
            {"Resources":{"api":{"Type":"http.client","BaseAddress":"{{{{server.Urls.Single()}}}}","Timeout":"00:00:02","Retry":{"MaxRetryAttempts":0}}},
             "Workflows":{"main":{
               "tick":{"Type":"scheduler","Interval":"00:00:00.050","Output":"once.Input"},
               "once":{"Type":"source","Values":"[{\"Method\":\"POST\",\"RequestUrl\":\"/submit\",\"Body\":\"trial\"}]","Output":"send.Input"},
               "send":{"Type":"http.client","Client":"api"}
             }}}
            """, new());
        var original = JsonSerializer.Serialize(request);
        var preparation = new EnvironmentPreparation([new HttpEnvironmentAdapter()], []);
        using var logging = ExecutionLogTests.CreateFactory();
        using var plugins = RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.Http.HttpPlugin());
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []));
        var first = Guid.NewGuid();
        var next = Guid.NewGuid();
        try
        {
            (await runner.StartAsync(first, request, token)).State.ShouldBe("Running");
            await partialBody.Task.WaitAsync(token);
            var stop = runner.StopAsync(first, token);
            (await runner.GetAsync(first, token)).State.ShouldBe("Stopping");
            if (!stallUntilTimeout) finishBody.SetResult();
            await stop.WaitAsync(token);
            var stopped = await runner.GetAsync(first, token);
            stopped.State.ShouldBe("Stopped");
            var firstResult = Responses(stopped).ShouldHaveSingleItem();
            if (stallUntilTimeout)
            {
                firstResult.GetProperty("Error").ValueKind.ShouldBe(JsonValueKind.Object);
                await disconnected.Task.WaitAsync(token);
            }
            else firstResult.GetProperty("Value").GetProperty("Body").GetString().ShouldBe("first-run");
            var retained = JsonSerializer.Serialize(stopped.Outputs);

            (await runner.StartAsync(next, request, token)).State.ShouldBe("Running");
            while (Responses(await runner.GetAsync(next, token)).Length == 0) await Task.Delay(25, token);
            await runner.StopAsync(next, token).WaitAsync(token);
            var rerun = await runner.GetAsync(next, token);
            rerun.State.ShouldBe("Stopped");
            Responses(rerun).ShouldHaveSingleItem().GetProperty("Value").GetProperty("Body").GetString().ShouldBe("next-run");
            JsonSerializer.Serialize((await runner.GetAsync(first, token)).Outputs).ShouldBe(retained);
            Volatile.Read(ref posts).ShouldBe(2);
            JsonSerializer.Serialize(request).ShouldBe(original);
        }
        finally
        {
            finishBody.TrySetResult();
            await runner.StopAsync(next, CancellationToken.None);
            await runner.StopAsync(first, CancellationToken.None);
        }
    }

    private static JsonElement[] Responses(EnvironmentRunStatus status) => status.Outputs!.Records
        .Where(record => record.Port == "main.send.Output")
        .Select(record => record.Message!.Value).ToArray();
}
