using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Execution;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class BatchRunTests
{
    [Theory]
    [InlineData(3, "01:00:00", false)]
    [InlineData(20, "00:00:00.080", true)]
    public async Task Size_deadline_and_Stop_preserve_envelopes_and_rerun_isolation(int size, string wait, bool waitForTimer)
    {
        using var plugins = RunTestPlugins.WithBasicComponents();
        using var logging = ExecutionLogTests.CreateFactory();
        var preparation = new EnvironmentPreparation([], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        var request = new EnvironmentRunRequest(JsonSerializer.Serialize(new
        {
            Workflows = new { main = new
            {
                tick = new { Type = "scheduler", Interval = "00:00:00.020", Output = "samples.Input" },
                samples = new { Type = "source", Values = "[null,1,\"two\",{\"keep\":9007199254740993},[4]]", Output = "batch.Input" },
                batch = new { Type = "batch", BatchSize = size, MaxWait = wait, OutputCapacity = 1, Output = "sink.Input" },
                sink = new { Type = "mapper", Engine = "jsonata", Expression = "$" }
            } }
        }), new());
        var original = JsonSerializer.Serialize(request);
        (await runner.CheckAsync(request, token)).Success.ShouldBeTrue();
        Guid previous = Guid.Empty;
        string? retained = null;
        var previousIds = new HashSet<string>();
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var id = Guid.NewGuid();
            try
            {
                (await runner.StartAsync(id, request, token)).State.ShouldBe("Running");
                while (true)
                {
                    var active = await runner.GetAsync(id, token);
                    var received = active.Outputs!.Records.Count(record => record.Port == "main.samples.Output");
                    var emitted = active.Outputs.Records.Where(record => record.Port == "main.batch.Output")
                        .Sum(record => record.Message!.Value.GetProperty("Value").GetArrayLength());
                    if (received == 5 && emitted == (waitForTimer ? 5 : 3)) break;
                    await Task.Delay(10, token);
                }
                await runner.StopAsync(id, token);
                var stopped = await runner.GetAsync(id, token);
                stopped.State.ShouldBe("Stopped", stopped.Error);
                var inputs = stopped.Outputs!.Records.Where(record => record.Port == "main.samples.Output")
                    .Select(record => record.Message!.Value).ToArray();
                var batches = stopped.Outputs.Records.Where(record => record.Port == "main.batch.Output")
                    .Select(record => record.Message!.Value).ToArray();
                var members = batches.SelectMany(batch => batch.GetProperty("Value").EnumerateArray()).ToArray();
                var delivered = stopped.Outputs.Records.Where(record => record.Port == "main.sink.Output")
                    .Select(record => record.Message!.Value).ToArray();
                delivered.Length.ShouldBe(batches.Length);
                for (var index = 0; index < batches.Length; index++)
                {
                    delivered[index].GetProperty("IsError").GetBoolean().ShouldBeFalse();
                    JsonNode.DeepEquals(JsonNode.Parse(delivered[index].GetProperty("Value").GetRawText()),
                        JsonNode.Parse(batches[index].GetProperty("Value").GetRawText())).ShouldBeTrue();
                }
                members.Length.ShouldBe(5);
                inputs.Length.ShouldBe(members.Length);
                for (var index = 0; index < members.Length; index++)
                    JsonNode.DeepEquals(JsonNode.Parse(members[index].GetRawText()), JsonNode.Parse(inputs[index].GetRawText())).ShouldBeTrue();
                members[3].GetProperty("Value").GetProperty("keep").GetInt64().ShouldBe(9007199254740993L);
                if (!waitForTimer) batches.Select(batch => batch.GetProperty("Value").GetArrayLength()).ShouldBe(new[] { 3, 2 });
                else batches.ShouldAllBe(batch => batch.GetProperty("Value").GetArrayLength() > 0 && batch.GetProperty("Value").GetArrayLength() < 20);
                foreach (var envelope in inputs.Concat(batches))
                    previousIds.Add(envelope.GetProperty("MessageId").GetString()!).ShouldBeTrue();
                batches.ShouldAllBe(batch => batch.GetProperty("CorrelationId").ValueKind == JsonValueKind.Null);
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
}
