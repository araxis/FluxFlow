using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Execution;
using FluxFlow.Plugins.Http;
using FluxFlow.Plugins.Routing;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class GateRunTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reopened_controls_take_effect_before_the_next_input_and_reset_on_rerun(bool initiallyOpen)
    {
        var configuration = """
            {"Workflows":{"main":{
              "open":{"Type":"http.trigger","Route":"/open","Output":"openSignal.Input"},
              "openSignal":{"Type":"mapper","Engine":"jsonata","Expression":"$error('open signal')"},
              "close":{"Type":"http.trigger","Route":"/close","Output":"trialGate.Close"},
              "data":{"Type":"http.trigger","Route":"/data","Output":"reading.Input"},
              "reading":{"Type":"mapper","Engine":"jsonata","Expression":"Body = 'bad' ? $error('bad reading') : Body","Output":"trialGate.Input"},
              "trialGate":{"Type":"gate","InitiallyOpen":INITIAL,"Open":"openSignal.Output"}
            }}}
            """.Replace("INITIAL", JsonSerializer.Serialize(initiallyOpen));
        using var plugins = RunTestPlugins.WithBasicComponents(new HttpPlugin(), new RoutingPlugin());
        var catalog = plugins.Modules.SelectMany(m => m.Description.Components).ToArray();
        var graph = new DesignerGraphState(catalog);
        graph.LoadJson(configuration);
        var project = graph.ToProjectJson();
        var reopened = new DesignerGraphState(catalog);
        reopened.LoadProject(project);
        reopened.ResetHistory();
        var openWire = reopened.Diagram.Links.OfType<FlowLinkModel>().Single(l => l.Original.Property == "Open");
        reopened.Diagram.SelectModel(openWire, true);
        reopened.DeleteSelected();
        reopened.RecordEdit();
        var withoutOpen = reopened.ToJson();
        JsonNode.Parse(withoutOpen)!["Workflows"]!["main"]!["trialGate"]!.AsObject().ContainsKey("Open").ShouldBeFalse();
        reopened.Undo();
        reopened.ToProjectJson().ShouldBe(project);
        reopened.Redo();
        reopened.ToJson().ShouldBe(withoutOpen);
        reopened.Undo();
        var request = new EnvironmentRunRequest(reopened.ToJson(), new());
        JsonNode.DeepEquals(JsonNode.Parse(request.Configuration), JsonNode.Parse(configuration)).ShouldBeTrue();

        using var logging = ExecutionLogTests.CreateFactory();
        var preparation = new EnvironmentPreparation([], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        using var client = new HttpClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var firstRun = Guid.NewGuid();
        string? retained = null;
        var messageIds = new HashSet<string>();
        foreach (var id in new[] { firstRun, Guid.NewGuid() })
        {
            using var processed = new SemaphoreSlim(0);
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "FluxFlow.Core",
                Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                    options.Name == "main.trialGate" ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None,
                ActivityStopped = activity =>
                {
                    if (activity.OperationName == "main.trialGate") processed.Release();
                }
            };
            ActivitySource.AddActivityListener(listener);
            try
            {
                var started = await runner.StartAsync(id, request, token);
                started.State.ShouldBe("Running", started.Error);
                var url = HttpTriggerRunTests.ListenerAddresses(started).Single();
                async Task Send(string path, string body)
                {
                    using var content = new StringContent(body);
                    using var response = await client.PostAsync(url + path, content, token);
                    response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
                    // The HTTP acknowledgement precedes processing. A finished Gate span is the barrier
                    // for both control commands and data that Gate may legitimately discard.
                    await processed.WaitAsync(token);
                }
                for (var phase = 0; phase < 4; phase++)
                {
                    var open = phase % 2 == 0 ? initiallyOpen : !initiallyOpen;
                    if (phase > 0) await Send(open ? "/open" : "/close", "control");
                    await Send("/data", $"reading-{phase}");
                    await Send("/data", "bad");
                }
            }
            finally { await runner.StopAsync(id, CancellationToken.None); }
            var stopped = await runner.GetAsync(id, token);
            stopped.State.ShouldBe("Stopped", stopped.Error);
            JsonElement[] Output(string port) => stopped.Outputs!.Records.Where(r => r.Port == port).Select(r => r.Message!.Value).ToArray();
            var input = Output("main.reading.Output");
            input.Length.ShouldBe(8);
            var openSignals = Output("main.openSignal.Output");
            openSignals.Length.ShouldBe(initiallyOpen ? 1 : 2);
            openSignals.ShouldAllBe(m => m.GetProperty("IsError").GetBoolean());
            for (var phase = 0; phase < 4; phase++)
            {
                input[2 * phase].GetProperty("Value").GetString().ShouldBe($"reading-{phase}");
                input[2 * phase + 1].GetProperty("Error").GetRawText().ShouldContain("bad reading");
            }
            foreach (var message in input) messageIds.Add(message.GetProperty("MessageId").GetString()!).ShouldBeTrue();
            var expected = input.Where((_, index) => (index / 2 % 2 == 0) == initiallyOpen);
            Output("main.trialGate.Output").Select(m => m.GetRawText()).ShouldBe(expected.Select(m => m.GetRawText()));
            if (id == firstRun) retained = JsonSerializer.Serialize(stopped.Outputs);
            else JsonSerializer.Serialize((await runner.GetAsync(firstRun, token)).Outputs).ShouldBe(retained);
        }
        reopened.ToProjectJson().ShouldBe(project);
    }
}
