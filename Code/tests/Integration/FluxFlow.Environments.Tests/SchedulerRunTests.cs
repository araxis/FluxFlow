using System.Text.Json;
using FluxFlow.Execution;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class SchedulerRunTests
{
    [Theory]
    [InlineData("\"Interval\":\"00:00:00.020\"", true)]
    [InlineData("\"Cron\":\"* * * * * *\",\"IncludeSeconds\":true,\"TimeZone\":\"Europe/Stockholm\"", true)]
    [InlineData("\"At\":\"2099-01-01T00:00:00+05:30\"", false)]
    [InlineData("\"At\":\"2000-01-01T00:00:00Z\"", false)]
    public async Task Stop_and_rerun_isolate_signals_and_cancel_pending_schedules(string schedule, bool emits)
    {
        using var plugins = RunTestPlugins.WithBasicComponents();
        using var logging = ExecutionLogTests.CreateFactory();
        var preparation = new EnvironmentPreparation([], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        var request = new EnvironmentRunRequest("{\"Workflows\":{\"main\":{\"tick\":{\"Type\":\"scheduler\"," + schedule + "}}}}", new());
        var original = JsonSerializer.Serialize(request);
        (await runner.CheckAsync(request, token)).Success.ShouldBeTrue();
        Guid previous = Guid.Empty;
        string? retained = null;
        string? previousMessage = null;
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var id = Guid.NewGuid();
            try
            {
                (await runner.StartAsync(id, request, token)).State.ShouldBe("Running");
                if (emits)
                    while (!(await runner.GetAsync(id, token)).Outputs!.Records.Any(r => r.Port == "main.tick.Output"))
                        await Task.Delay(10, token);
                await runner.StopAsync(id, token);
                var stopped = await runner.GetAsync(id, token);
                stopped.State.ShouldBe("Stopped", stopped.Error);
                var outputs = stopped.Outputs!.Records.Where(r => r.Port == "main.tick.Output").ToArray();
                if (emits)
                {
                    outputs.ShouldNotBeEmpty();
                    var message = outputs[0].Message!.Value;
                    message.GetProperty("Value").ValueKind.ShouldBe(JsonValueKind.Null);
                    var messageId = message.GetProperty("MessageId").GetString();
                    messageId.ShouldNotBe(previousMessage);
                    previousMessage = messageId;
                }
                else outputs.ShouldBeEmpty();
                if (retained is not null)
                    JsonSerializer.Serialize((await runner.GetAsync(previous, token)).Outputs).ShouldBe(retained);
                previous = id; retained = JsonSerializer.Serialize(stopped.Outputs);
                await runner.StopAsync(id, token);
                JsonSerializer.Serialize((await runner.GetAsync(id, token)).Outputs).ShouldBe(retained);
            }
            finally { await runner.StopAsync(id, CancellationToken.None); }
        }
        JsonSerializer.Serialize(request).ShouldBe(original);
    }

    [Fact]
    public async Task Unknown_runner_time_zone_fails_check_without_changing_configuration()
    {
        using var plugins = RunTestPlugins.WithBasicComponents();
        using var logging = ExecutionLogTests.CreateFactory();
        var preparation = new EnvironmentPreparation([], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        var request = new EnvironmentRunRequest("""{"Workflows":{"main":{"tick":{"Type":"scheduler","Cron":"* * * * *","TimeZone":"Missing/RunnerZone"}}}}""", new());
        var original = JsonSerializer.Serialize(request);
        (await runner.CheckAsync(request, CancellationToken.None)).Success.ShouldBeFalse();
        JsonSerializer.Serialize(request).ShouldBe(original);
    }
}
