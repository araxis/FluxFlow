using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Execution;
using FluxFlow.Plugins.Routing;
using FluxFlow.Studio.UI.Features.Designer;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class RouterRunTests
{
    [Fact]
    public async Task Reopened_named_routes_deliver_all_matches_and_isolate_failures_across_runs()
    {
        const string configuration = """
            {"Workflows":{"main":{
              "tick":{"Type":"scheduler","Interval":"00:00:00.020","Output":"samples.Input"},
              "samples":{"Type":"source","Values":"[2,-1,13,3,0,4]","Output":"route.Input"},
              "route":{"Type":"router","Engine":"jsonata",
                "Routes":{"Positive":"$ > 0","Secondary":"$ = 13 ? $error('invalid reading') : $ % 2 = 0"},
                "Secondary":"evenHits.Input","Positive":"positiveHits.Input",
                "Unmatched":"misses.Input","Output":"errors.Input"},
              "positiveHits":{"Type":"gate","InitiallyOpen":true},
              "evenHits":{"Type":"gate","InitiallyOpen":true},
              "misses":{"Type":"gate","InitiallyOpen":true},
              "errors":{"Type":"gate","InitiallyOpen":true}
            }}}
            """;
        using var plugins = RunTestPlugins.WithBasicComponents(new RoutingPlugin());
        var catalog = plugins.Modules.SelectMany(m => m.Description.Components).ToArray();
        var graph = new DesignerGraphState(catalog);
        graph.LoadJson(configuration);
        var project = graph.ToProjectJson();
        var reopened = new DesignerGraphState(catalog);
        reopened.LoadProject(project);
        var request = new EnvironmentRunRequest(reopened.ToJson(), new());
        JsonNode.DeepEquals(JsonNode.Parse(request.Configuration), JsonNode.Parse(configuration)).ShouldBeTrue();

        using var logging = ExecutionLogTests.CreateFactory();
        var preparation = new EnvironmentPreparation([], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        (await runner.CheckAsync(request, token)).Success.ShouldBeTrue();
        var firstRun = Guid.NewGuid();
        string? retained = null;
        var messageIds = new HashSet<string>();
        foreach (var id in new[] { firstRun, Guid.NewGuid() })
        {
            try
            {
                var started = await runner.StartAsync(id, request, token);
                started.State.ShouldBe("Running", started.Error);
                while ((await runner.GetAsync(id, token)).Outputs!.Records.Count(r => r.Port == "main.samples.Output") < 6)
                    await Task.Delay(10, token);
            }
            finally { await runner.StopAsync(id, CancellationToken.None); }

            var stopped = await runner.GetAsync(id, token);
            stopped.State.ShouldBe("Stopped", stopped.Error);
            JsonElement[] Messages(string port) => stopped.Outputs!.Records.Where(r => r.Port == port)
                .Select(r => r.Message!.Value).ToArray();
            var inputs = Messages("main.samples.Output").ToDictionary(m => m.GetProperty("Value").GetInt32());
            inputs.Count.ShouldBe(6);
            foreach (var input in inputs.Values) messageIds.Add(input.GetProperty("MessageId").GetString()!).ShouldBeTrue();

            void Expect(string port, params int[] values) => Messages(port).Select(m => m.GetRawText())
                .ShouldBe(values.Select(value => inputs[value].GetRawText()));
            Expect("main.positiveHits.Output", 2, 3, 4);
            Expect("main.evenHits.Output", 2, 0, 4);
            Expect("main.misses.Output", -1);
            var error = Messages("main.route.Output").Single();
            error.GetProperty("IsError").GetBoolean().ShouldBeTrue();
            error.GetProperty("Error").GetRawText().ShouldContain("invalid reading");
            error.GetProperty("CausationId").GetString().ShouldBe(inputs[13].GetProperty("MessageId").GetString());
            Messages("main.errors.Output").Single().GetRawText().ShouldBe(error.GetRawText());
            if (id == firstRun) retained = JsonSerializer.Serialize(stopped.Outputs);
            else JsonSerializer.Serialize((await runner.GetAsync(firstRun, token)).Outputs).ShouldBe(retained);
        }
        reopened.ToProjectJson().ShouldBe(project);
    }
}
