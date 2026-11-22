using System.Text.Json;
using FluxFlow.Execution;
using FluxFlow.Plugins.Routing;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class BusinessKeyFilterRunTests
{
    private const string Selector = "key = null ? $error('missing business key') : key";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Deduplicate_evicts_the_oldest_key_and_starts_fresh_on_rerun(bool ignoreCase) =>
        AssertRuns(new { Type = "deduplicate", Engine = "jsonata", KeySelector = Selector,
            IgnoreCase = ignoreCase, MaxKeys = 2, Retention = "01:00:00" },
            ignoreCase ? [0, 1, 6, 7] : [0, 1, 5, 6, 7]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Change_filter_remembers_only_the_last_forwarded_key_and_starts_fresh_on_rerun(bool ignoreCase) =>
        AssertRuns(new { Type = "changeFilter", Engine = "jsonata", KeySelector = Selector, IgnoreCase = ignoreCase },
            ignoreCase ? [0, 1, 4, 6, 7] : [0, 1, 4, 5, 6, 7]);

    private static async Task AssertRuns(object filter, int[] forwarded)
    {
        string?[] keys = ["A", "B", null, "B", "A", "a", "C", "A", "A"];
        var samples = keys.Select((key, sequence) => new { key, sequence });
        var request = new EnvironmentRunRequest(JsonSerializer.Serialize(new
        {
            Workflows = new { main = new
            {
                tick = new { Type = "scheduler", Interval = "00:00:00.020", Output = "samples.Input" },
                samples = new { Type = "source", Values = JsonSerializer.Serialize(samples), Output = "filter.Input" },
                filter
            } }
        }), new());
        using var plugins = RunTestPlugins.WithBasicComponents(new RoutingPlugin());
        using var logging = ExecutionLogTests.CreateFactory();
        var preparation = new EnvironmentPreparation([], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        (await runner.CheckAsync(request, token)).Success.ShouldBeTrue();

        var firstRun = Guid.NewGuid();
        string? firstObservations = null;
        var inputIds = new HashSet<string>();
        foreach (var id in new[] { firstRun, Guid.NewGuid() })
        {
            try
            {
                var started = await runner.StartAsync(id, request, token);
                started.State.ShouldBe("Running", started.Error);
                while ((await runner.GetAsync(id, token)).Outputs!.Records.Count(r => r.Port == "main.samples.Output") < keys.Length)
                    await Task.Delay(10, token);
            }
            finally { await runner.StopAsync(id, CancellationToken.None); }

            var stopped = await runner.GetAsync(id, token);
            stopped.State.ShouldBe("Stopped", stopped.Error);
            var records = stopped.Outputs!.Records;
            var inputs = records.Where(r => r.Port == "main.samples.Output").Select(r => r.Message!.Value).ToArray();
            inputs.Length.ShouldBe(keys.Length);
            foreach (var input in inputs) inputIds.Add(input.GetProperty("MessageId").GetString()!).ShouldBeTrue();
            var outputs = records.Where(r => r.Port == "main.filter.Output").Select(r => r.Message!.Value).ToArray();
            var error = outputs.Single(m => m.GetProperty("IsError").GetBoolean());
            error.GetProperty("Error").GetRawText().ShouldContain("missing business key");
            error.GetProperty("CausationId").GetString().ShouldBe(inputs[2].GetProperty("MessageId").GetString());
            outputs.Where(m => !m.GetProperty("IsError").GetBoolean()).Select(m => m.GetRawText())
                .ShouldBe(forwarded.Select(index => inputs[index].GetRawText()));
            if (id == firstRun) firstObservations = JsonSerializer.Serialize(stopped.Outputs);
            else JsonSerializer.Serialize((await runner.GetAsync(firstRun, token)).Outputs).ShouldBe(firstObservations);
        }
    }
}
