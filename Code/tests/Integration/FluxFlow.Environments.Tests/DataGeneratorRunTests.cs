using System.Text.Json;
using FluxFlow.Execution;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class DataGeneratorRunTests
{
    [Fact]
    public async Task Stop_while_scheduler_is_active_retains_results_and_rerun_is_isolated()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        using var plugins = RunTestPlugins.WithBasicComponents();
        using var logging = ExecutionLogTests.CreateFactory();
        var preparation = new EnvironmentPreparation([], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        var request = new EnvironmentRunRequest("""
            {"Workflows":{"main":{
              "tick":{"Type":"scheduler","Interval":"00:00:00.020","Output":"generate.Input"},
              "generate":{"Type":"dataGenerator","Locale":"sv","Expression":"{'number':$fakeInt(7,7),'source':$messageId}"}
            }}}
            """, new());
        var original = JsonSerializer.Serialize(request);
        Guid previous = Guid.Empty;
        string? retained = null;
        var previousIds = new HashSet<string?>();
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var id = Guid.NewGuid();
            try
            {
                (await runner.StartAsync(id, request, token)).State.ShouldBe("Running");
                while ((await runner.GetAsync(id, token)).Outputs!.Records.Count(record => record.Port == "main.generate.Output") < 3)
                    await Task.Delay(10, token);
                await runner.StopAsync(id, token);
                var stopped = await runner.GetAsync(id, token);
                stopped.State.ShouldBe("Stopped", stopped.Error);
                var results = stopped.Outputs!.Records.Where(record => record.Port == "main.generate.Output")
                    .Select(record => record.Message!.Value).ToArray();
                results.Length.ShouldBeGreaterThanOrEqualTo(3);
                var ids = new HashSet<string?>();
                foreach (var result in results)
                {
                    result.GetProperty("IsError").GetBoolean().ShouldBeFalse();
                    result.GetProperty("Value").GetProperty("number").GetInt32().ShouldBe(7);
                    ids.Add(result.GetProperty("Value").GetProperty("source").GetString());
                }
                ids.Overlaps(previousIds).ShouldBeFalse();
                if (retained is not null)
                    JsonSerializer.Serialize((await runner.GetAsync(previous, token)).Outputs).ShouldBe(retained);
                previous = id;
                retained = JsonSerializer.Serialize(stopped.Outputs);
                previousIds = ids;
                // Repeated Stop must neither restart generation nor discard retained records.
                await runner.StopAsync(id, token);
                JsonSerializer.Serialize((await runner.GetAsync(id, token)).Outputs).ShouldBe(retained);
            }
            finally { await runner.StopAsync(id, CancellationToken.None); }
        }
        JsonSerializer.Serialize(request).ShouldBe(original);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("sv")]
    public async Task Normal_run_recovers_from_generation_errors_and_reruns_without_retained_state(string locale)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        using var plugins = RunTestPlugins.WithBasicComponents();
        using var logging = ExecutionLogTests.CreateFactory();
        var preparation = new EnvironmentPreparation([], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        var request = new EnvironmentRunRequest("""
            {"Workflows":{"main":{
              "tick":{"Type":"scheduler","Interval":"00:00:00.030","Output":"source.Input"},
              "source":{"Type":"source","Values":"[{\"bad\":true},{\"bad\":false}]","Output":"generate.Input"},
              "generate":{"Type":"dataGenerator","Locale":"LOCALE","Expression":"bad ? $fakeInt(9,1) : {\"number\":$fakeInt(7,7),\"name\":$fakeFirstName(),\"source\":$messageId}"}
            }}}
            """.Replace("LOCALE", locale), new());
        var original = JsonSerializer.Serialize(request);
        (await runner.CheckAsync(request, token)).Success.ShouldBeTrue();
        string? retained = null;
        Guid previous = Guid.Empty;
        string? previousSourceId = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var id = Guid.NewGuid();
            try
            {
                var running = await runner.StartAsync(id, request, token);
                running.State.ShouldBe("Running", running.Error);
                while ((await runner.GetAsync(id, token)).Outputs!.Records.Count(record => record.Port == "main.generate.Output") < 2)
                    await Task.Delay(10, token);
                await runner.StopAsync(id, token);
                var stopped = await runner.GetAsync(id, token);
                stopped.State.ShouldBe("Stopped", stopped.Error);
                var output = stopped.Outputs!.Records.Where(record => record.Port == "main.generate.Output").Select(record => record.Message!.Value).ToArray();
                output.Length.ShouldBe(2);
                output[0].GetProperty("IsError").GetBoolean().ShouldBeTrue();
                output[1].GetProperty("IsError").GetBoolean().ShouldBeFalse();
                var generated = output[1].GetProperty("Value");
                generated.GetProperty("number").GetInt32().ShouldBe(7);
                generated.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
                var sourceId = generated.GetProperty("source").GetString();
                sourceId.ShouldBe(output[1].GetProperty("CausationId").GetString());
                if (retained != null)
                {
                    JsonSerializer.Serialize((await runner.GetAsync(previous, token)).Outputs).ShouldBe(retained);
                    sourceId.ShouldNotBe(previousSourceId);
                }
                previousSourceId = sourceId;
                retained = JsonSerializer.Serialize(stopped.Outputs);
                previous = id;
            }
            finally { await runner.StopAsync(id, CancellationToken.None); }
        }
        JsonSerializer.Serialize(request).ShouldBe(original);
    }

    [Fact]
    public async Task Invalid_locale_is_rejected_before_start_without_modifying_configuration()
    {
        using var plugins = RunTestPlugins.WithBasicComponents();
        using var logging = ExecutionLogTests.CreateFactory();
        var preparation = new EnvironmentPreparation([], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        var request = new EnvironmentRunRequest("""{"Workflows":{"main":{"generate":{"Type":"dataGenerator","Expression":"42","Locale":"not-a-locale"}}}}""", new());
        var original = JsonSerializer.Serialize(request);
        var check = await runner.CheckAsync(request, CancellationToken.None);
        check.Success.ShouldBeFalse(); check.Message.ShouldContain("Locale"); check.Message.ShouldContain("main.generate");
        JsonSerializer.Serialize(request).ShouldBe(original);
    }
}
