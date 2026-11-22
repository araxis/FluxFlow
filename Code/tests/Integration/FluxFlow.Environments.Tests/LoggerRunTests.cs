using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Execution;
using FluxFlow.Studio.UI.Features.Designer;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class LoggerRunTests
{
    [Fact]
    public async Task Mixed_batch_outputs_and_captured_logs_survive_Stop_and_stay_isolated_on_rerun()
    {
        const string samples = """
            [
              {"request":[
                {"LogLevel":"Information","MessageTemplate":"Order {Id:D4}","Args":[21],"Attributes":{"OnLine":true}},
                {"LogLevel":"Warning","MessageTemplate":"Missing {Value}"},
                {"LogLevel":"Warning","MessageTemplate":"Station {Name}","Args":["West"]}
              ]},
              {},
              {"request":{"LogLevel":"Debug","MessageTemplate":"Below capture level"}}
            ]
            """;
        var configuration = JsonSerializer.Serialize(new { Workflows = new { main = new
        {
            tick = new { Type = "scheduler", Interval = "00:00:00.020", Output = "samples.Input" },
            samples = new { Type = "source", Values = samples, Output = "prepare.Input" },
            prepare = new { Type = "mapper", Engine = "jsonata", Expression = "$exists(request) ? request : $error('upstream marker')", Output = "record.Input" },
            record = new { Type = "logger", Category = "Trial.Orders" }
        } } });
        using var plugins = RunTestPlugins.WithBasicComponents();
        var catalog = plugins.Modules.SelectMany(m => m.Description.Components).ToArray();
        var graph = new DesignerGraphState(catalog);
        graph.LoadJson(configuration);
        var saved = graph.ToProjectJson();
        var reopened = new DesignerGraphState(catalog);
        reopened.LoadProject(saved);
        var request = new EnvironmentRunRequest(reopened.ToJson(), new());
        JsonNode.DeepEquals(JsonNode.Parse(request.Configuration), JsonNode.Parse(configuration)).ShouldBeTrue();

        using var logging = ExecutionLogTests.CreateFactory(minimum: LogLevel.Information);
        var preparation = new EnvironmentPreparation([], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        (await runner.CheckAsync(request, token)).Success.ShouldBeTrue();
        var firstRun = Guid.NewGuid();
        string? retainedOutputs = null, retainedLogs = null;
        var ids = new HashSet<string>();
        foreach (var id in new[] { firstRun, Guid.NewGuid() })
        {
            try
            {
                var started = await runner.StartAsync(id, request, token);
                started.State.ShouldBe("Running", started.Error);
                while ((await runner.GetAsync(id, token)).Outputs!.Records.Count(r => r.Port == "main.samples.Output") < 3)
                    await Task.Delay(10, token);
            }
            finally { await runner.StopAsync(id, CancellationToken.None); }
            var stopped = await runner.GetAsync(id, token);
            stopped.State.ShouldBe("Stopped", stopped.Error);
            JsonElement[] Messages(string port) => stopped.Outputs!.Records.Where(r => r.Port == port)
                .Select(r => r.Message!.Value).ToArray();
            var inputs = Messages("main.prepare.Output");
            inputs.Length.ShouldBe(3);
            var outputs = Messages("main.record.Output");
            outputs.Select(m => m.GetProperty("IsError").GetBoolean()).ShouldBe([false, true, false, true, false]);
            foreach (var output in outputs) ids.Add(output.GetProperty("MessageId").GetString()!).ShouldBeTrue();
            outputs[0].GetProperty("Value").GetProperty("MessageTemplate").GetString().ShouldBe("Order {Id:D4}");
            outputs[1].GetProperty("Error").GetRawText().ShouldContain("placeholders");
            outputs[2].GetProperty("Value").GetProperty("MessageTemplate").GetString().ShouldBe("Station {Name}");
            outputs.Take(3).ShouldAllBe(m => m.GetProperty("TraceId").GetString() == inputs[0].GetProperty("TraceId").GetString());
            inputs[1].GetProperty("Error").GetRawText().ShouldContain("upstream marker");
            outputs[3].GetRawText().ShouldBe(inputs[1].GetRawText());
            outputs[4].GetProperty("Value").GetProperty("MessageTemplate").GetString().ShouldBe("Below capture level");

            var logs = stopped.Logs!.Records.Where(r => r.Category == "Trial.Orders").ToArray();
            logs.Select(r => r.Message).ShouldBe(["Order 0021", "Station West"]);
            logs.Select(r => r.Level).ShouldBe(["Information", "Warning"]);
            var attributes = logs[0].Telemetry!.Attributes;
            attributes.Single(a => a.Name == "{OriginalFormat}").Value.GetString().ShouldBe("Order {Id:D4}");
            attributes.Single(a => a.Name == "Id").Value.GetInt64().ShouldBe(21);
            attributes.Single(a => a.Name == "OnLine").Value.GetBoolean().ShouldBeTrue();
            logs[1].Telemetry!.Attributes.ShouldNotContain(a => a.Name == "OnLine");
            if (id == firstRun)
            {
                retainedOutputs = JsonSerializer.Serialize(stopped.Outputs);
                retainedLogs = JsonSerializer.Serialize(stopped.Logs);
            }
            else
            {
                var previous = await runner.GetAsync(firstRun, token);
                JsonSerializer.Serialize(previous.Outputs).ShouldBe(retainedOutputs);
                JsonSerializer.Serialize(previous.Logs).ShouldBe(retainedLogs);
            }
        }
        reopened.ToProjectJson().ShouldBe(saved);
    }
}
