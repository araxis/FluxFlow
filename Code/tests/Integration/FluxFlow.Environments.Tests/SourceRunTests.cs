using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class SourceRunTests
{
    private const string Values = "[null,9007199254740993,\"two\",true,{\"keep\":42},[4]]";

    [Fact]
    public async Task Competing_triggers_share_one_last_value_and_exhausted_errors_survive_stop_and_rerun()
    {
        using var config = WorkflowExecution.ReadConfiguration("""
            {"Workflows":{"main":{"samples":{"Type":"source","Values":"[17]"}}}}
            """);
        using var plugins = RunTestPlugins.WithBasicComponents();
        var services = new ServiceCollection();
        WorkflowExecution.Register(services, config, plugins);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        await app.StartAsync(token);
        var workflow = provider.GetRequiredKeyedService<FlowWorkflow>("main");
        for (var run = 0; run < 2; run++)
        {
            if (run == 1) await workflow.StartAsync(token);
            var input = workflow.Services.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.samples.Input");
            var outputs = new BufferBlock<FlowMessage>();
            using var link = workflow.Services.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.samples.Output")
                .LinkTo(outputs, new DataflowLinkOptions { PropagateCompletion = true });
            FlowMessage[] triggers = run == 0 ? [Trigger("left"), Trigger("right")] : [Trigger("fresh")];
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var sends = triggers.Select(async trigger =>
            {
                await release.Task.ConfigureAwait(false);
                (await input.SendAsync(trigger, token)).ShouldBeTrue();
            }).ToArray();
            release.SetResult();
            await Task.WhenAll(sends).WaitAsync(token);

            var value = await outputs.ReceiveAsync(token);
            value.Value.Json.GetInt32().ShouldBe(17);
            var winner = triggers.Single(trigger => trigger.MessageId == value.CausationId);
            value.TraceId.ShouldBe(winner.TraceId);
            value.CorrelationId.ShouldBe(winner.CorrelationId);
            value.Headers["origin"].ShouldBeSameAs(winner.Headers["origin"]);
            value.MessageId.ShouldNotBe(winner.MessageId);

            var error = Trigger("upstream").WithValue(new FlowError("sample.failed", "Upstream failure", "test"));
            (await input.SendAsync(error, token)).ShouldBeTrue();
            await workflow.StopAsync(token);
            (await outputs.ReceiveAsync(token)).ShouldBeSameAs(error);
            (await outputs.OutputAvailableAsync(token)).ShouldBeFalse();
            (await input.SendAsync(error, token)).ShouldBeFalse();
        }

        static FlowMessage Trigger(string origin) => FlowMessage.Create(JsonFlowValue.Null, CorrelationId.New(),
            headers: new Dictionary<string, FlowValue> { ["origin"] = origin });
    }

    [Theory]
    [InlineData("inline")]
    [InlineData("relative")]
    [InlineData("absolute")]
    [InlineData("implicit")]
    [InlineData("empty")]
    [InlineData("partial")]
    public async Task Loading_exhaustion_and_rerun_preserve_values_lineage_and_run_isolation(string mode)
    {
        var directory = Directory.CreateTempSubdirectory("fluxflow-source-run-");
        var path = Path.Combine(directory.FullName, "sample data");
        using var plugins = RunTestPlugins.WithBasicComponents();
        using var logging = ExecutionLogTests.CreateFactory();
        var preparation = new EnvironmentPreparation([], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        var fileMode = mode is "relative" or "absolute" or "implicit";
        var reference = mode switch
        {
            "relative" => " FILE:" + Path.GetRelativePath(AppContext.BaseDirectory, path) + " ",
            "absolute" => "file:" + path,
            "implicit" => path,
            "empty" => "[]",
            "partial" => JsonSerializer.Serialize(Enumerable.Range(0, 1000)),
            _ => " INLINE: " + Values + " "
        };
        var request = Request(reference);
        var original = JsonSerializer.Serialize(request);
        Guid previous = Guid.Empty;
        string? retained = null;
        var identities = new HashSet<string>();
        try
        {
            for (var iteration = 0; iteration < 2; iteration++)
            {
                var values = mode == "empty" ? "[]" : mode == "partial" ? reference : fileMode && iteration == 1 ? "[\"reloaded\",false]" : Values;
                if (fileMode) File.WriteAllText(path, values, new UTF8Encoding(true));
                (await runner.CheckAsync(request, token)).Success.ShouldBeTrue();
                using var document = JsonDocument.Parse(values);
                var expected = document.RootElement.EnumerateArray().ToArray();
                var id = Guid.NewGuid();
                try
                {
                    (await runner.StartAsync(id, request, token)).State.ShouldBe("Running");
                    // Neither the active run nor Stop may reopen an already snapshotted file.
                    if (fileMode) File.Delete(path);
                    while ((await runner.GetAsync(id, token)).Outputs!.Records.Count(record => record.Port == "main.tick.Output") < (mode == "partial" ? 1 : expected.Length + 3))
                        await Task.Delay(10, token);
                    await runner.StopAsync(id, token);
                    var stopped = await runner.GetAsync(id, token);
                    stopped.State.ShouldBe("Stopped", stopped.Error);
                    var triggers = stopped.Outputs!.Records.Where(record => record.Port == "main.tick.Output")
                        .Select(record => record.Message!.Value).ToArray();
                    var outputs = stopped.Outputs.Records.Where(record => record.Port == "main.samples.Output")
                        .Select(record => record.Message!.Value).ToArray();
                    if (mode == "partial") outputs.Length.ShouldBeInRange(1, expected.Length - 1);
                    else outputs.Length.ShouldBe(expected.Length);
                    for (var index = 0; index < outputs.Length; index++)
                    {
                        outputs[index].GetProperty("IsError").GetBoolean().ShouldBeFalse();
                        JsonNode.DeepEquals(JsonNode.Parse(outputs[index].GetProperty("Value").GetRawText()), JsonNode.Parse(expected[index].GetRawText())).ShouldBeTrue();
                        outputs[index].GetProperty("CausationId").GetString().ShouldBe(triggers[index].GetProperty("MessageId").GetString());
                        outputs[index].GetProperty("TraceId").GetString().ShouldBe(triggers[index].GetProperty("TraceId").GetString());
                        identities.Add(outputs[index].GetProperty("MessageId").GetString()!).ShouldBeTrue();
                    }
                    if (expected.Length == 6) outputs[1].GetProperty("Value").GetInt64().ShouldBe(9007199254740993L);
                    if (retained is not null) JsonSerializer.Serialize((await runner.GetAsync(previous, token)).Outputs).ShouldBe(retained);
                    previous = id; retained = JsonSerializer.Serialize(stopped.Outputs);
                    await runner.StopAsync(id, token);
                    JsonSerializer.Serialize((await runner.GetAsync(id, token)).Outputs).ShouldBe(retained);
                }
                finally { await runner.StopAsync(id, CancellationToken.None); }
            }
            JsonSerializer.Serialize(request).ShouldBe(original);
        }
        finally { File.Delete(path); directory.Delete(); }
    }

    [Theory]
    [InlineData("missing", "FileNotFoundException")]
    [InlineData("malformed", "JsonReaderException")]
    [InlineData("object", "Values must contain a JSON array")]
    [InlineData("deleted-after-check", "FileNotFoundException")]
    public async Task File_failure_is_preserved_and_Stop_allows_a_repaired_rerun(string failure, string expectedError)
    {
        var directory = Directory.CreateTempSubdirectory("fluxflow-source-failure-");
        var path = Path.Combine(directory.FullName, "values.json");
        using var plugins = RunTestPlugins.WithBasicComponents();
        using var logging = ExecutionLogTests.CreateFactory();
        var preparation = new EnvironmentPreparation([], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        var request = Request("file:" + path);
        var original = JsonSerializer.Serialize(request);
        var failedId = Guid.NewGuid();
        var repairedId = Guid.NewGuid();
        try
        {
            if (failure != "missing") File.WriteAllText(path, failure switch { "malformed" => "[invalid", "object" => "{}", _ => "[1]" });
            (await runner.CheckAsync(request, token)).Success.ShouldBe(failure == "deleted-after-check");
            if (failure == "deleted-after-check") File.Delete(path);
            var failed = await runner.StartAsync(failedId, request, token);
            failed.State.ShouldBe("Failed");
            failed.Error.ShouldNotBeNull(); failed.Error.ShouldContain(expectedError);
            await runner.StopAsync(failedId, token);
            await runner.StopAsync(failedId, token);
            (await runner.GetAsync(failedId, token)).State.ShouldBe("Stopped");
            var retainedFailure = Encoding.UTF8.GetString(runner.ReadLogs(failedId, diagnostics: true).Content);
            retainedFailure.ShouldContain(expectedError);
            File.WriteAllText(path, "[\"repaired\"]");
            (await runner.CheckAsync(request, token)).Success.ShouldBeTrue();
            (await runner.StartAsync(repairedId, request, token)).State.ShouldBe("Running");
            while (!(await runner.GetAsync(repairedId, token)).Outputs!.Records.Any(record => record.Port == "main.samples.Output"))
                await Task.Delay(10, token);
            await runner.StopAsync(repairedId, token);
            var repaired = await runner.GetAsync(repairedId, token);
            repaired.State.ShouldBe("Stopped", repaired.Error);
            repaired.Outputs!.Records.Single(record => record.Port == "main.samples.Output").Message!.Value
                .GetProperty("Value").GetString().ShouldBe("repaired");
            Encoding.UTF8.GetString(runner.ReadLogs(failedId, diagnostics: true).Content).ShouldBe(retainedFailure);
            JsonSerializer.Serialize(request).ShouldBe(original);
        }
        finally
        {
            await runner.StopAsync(failedId, CancellationToken.None);
            await runner.StopAsync(repairedId, CancellationToken.None);
            File.Delete(path); directory.Delete();
        }
    }

    private static EnvironmentRunRequest Request(string values) => new(JsonSerializer.Serialize(new
    {
        Workflows = new { main = new
        {
            tick = new { Type = "scheduler", Interval = "00:00:00.020", Output = "samples.Input" },
            samples = new { Type = "source", Values = values, OutputCapacity = 1 }
        } }
    }), new());
}
