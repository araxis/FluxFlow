using System.Net;
using System.Text.Json;
using FluxFlow.Application.Addressing;
using FluxFlow.DesignerApp.Services;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Environments;
using Microsoft.AspNetCore.TestHost;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed partial class ContainerRunTests
{
    [Fact]
    public async Task Manual_MQTT_controller_preserves_command_results_and_stops_during_reconnect()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = deadline.Token;
        const string passwords = "/mosquitto/config/trial-passwords";
        await using var broker = await Broker(false).StartAsync(new()
        {
            Provider = "mosquitto",
            Files = new() { [passwords] = "", ["/mosquitto/config/mosquitto.conf"] =
                $"listener 1883\nallow_anonymous false\npassword_file {passwords}\npersistence false\n" }
        }, token);
        var container = broker.ContainerId!;
        await SetPassword("trial");
        var configuration = """
            {"Resources":{"broker":{"Type":"mqtt.client","Host":"127.0.0.1","Port":PORT,"ClientId":"controller-trial","Username":"test","Password":"trial","AutoConnect":false,
              "Reconnect":{"MaxRetryAttempts":30,"Delay":"00:00:00.250","UseJitter":false}}},
             "Workflows":{"main":{
               "command":{"Type":"http.trigger","Route":"/command","Output":"decode.Input"},
               "decode":{"Type":"mapper","Engine":"jsonata","Expression":"{'Command':Body,'DisableReconnect':true}","Output":"controller.Input"},
               "controller":{"Type":"mqtt.connection","Connection":"broker"},
               "receive":{"Type":"mqtt.subscriber","Connection":"broker","Topics":[{"Topic":"trial/data","QoS":1}]}
             }}}
            """.Replace("PORT", broker.Endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var saved = CreateGraph(configuration).ToProjectJson();
        var reopened = CreateGraph(configuration);
        reopened.LoadProject(saved);
        reopened.ResetHistory();
        var commandWire = reopened.Diagram.Links.OfType<FlowLinkModel>()
            .Single(link => link.Original.Target == PortAddress.Parse("main.controller.Input"));
        reopened.Diagram.SelectModel(commandWire, true);
        reopened.DeleteSelected();
        reopened.RecordEdit();
        var withoutCommandWire = reopened.ToProjectJson();
        withoutCommandWire.ShouldNotBe(saved);
        reopened.Undo();
        reopened.ToProjectJson().ShouldBe(saved);
        await using var app = await CreateCompanion();
        using var api = app.GetTestClient();
        var runner = new HttpEnvironmentRunner(api);
        await using var session = new StudioRunSession(runner);
        using var http = new HttpClient();
        EnvironmentRunStatus? previous = null;
        for (var run = 1; run <= 2; run++)
        {
            await session.StartAsync(reopened).WaitAsync(token);
            var started = session.Status!;
            started.State.ShouldBe("Running", started.Error);
            var url = HttpTriggerRunTests.ListenerAddresses(started).Single() + "/command";
            try
            {
                Events(started).ShouldBeEmpty();
                await Command("Connect", 1);
                await Publish($"first-{run}");
                await WaitFor(s => Deliveries(s).Contains($"first-{run}"));
                await Command("Disconnect", 2);
                await WaitFor(s => Events(s).Contains("mqtt.disconnected"));
                await Publish("while-disconnected");
                await Command("Connect", 3);
                await Publish($"second-{run}");
                var delivered = await WaitFor(s => Deliveries(s).Contains($"second-{run}"));
                Deliveries(delivered).ShouldBe(new[] { $"first-{run}", $"second-{run}" });
                Events(delivered).ShouldBe(new[] { "mqtt.connecting", "mqtt.connected", "mqtt.disconnecting",
                    "mqtt.disconnected", "mqtt.connecting", "mqtt.connected" });
                await TakeOver("trial");
                await WaitFor(s => Messages(s, "receive.Events").Count(m =>
                    m.GetProperty("Value").GetProperty("Name").GetString() == "mqtt.subscriptions_applied") >= 3);
                await Publish($"recovered-{run}");
                await WaitFor(s => Deliveries(s).Contains($"recovered-{run}"));

                if (run == 1)
                {
                    // The broker stays reachable and rejects the controller's old password on reconnect.
                    await SetPassword("rotated");
                    await TakeOver("rotated");
                    await WaitFor(s => Messages(s, "controller.Output").Count(m => m.GetProperty("IsError").GetBoolean()
                        && m.GetProperty("Error").GetProperty("Code").GetString() == "mqtt.connection_failed") >= 2);
                }
            }
            finally { await session.StopAsync().WaitAsync(token); }
            session.CleanupError.ShouldBeNull();
            var stopped = await runner.GetAsync(started.Id, token);
            stopped.State.ShouldBe("Stopped", stopped.Error);
            Events(stopped).ShouldNotContain("mqtt.reconnect_exhausted");
            Deliveries(stopped).ShouldBe(new[] { $"first-{run}", $"second-{run}", $"recovered-{run}" });
            if (run == 1)
            {
                var attemptsAfterStop = await ConnectionAttempts();
                await SetPassword("trial");
                await Task.Delay(TimeSpan.FromSeconds(1), token);
                (await ConnectionAttempts()).ShouldBe(attemptsAfterStop);
            }
            if (previous != null)
            {
                var retained = await runner.GetAsync(previous.Id, token);
                JsonSerializer.Serialize(retained.Outputs).ShouldBe(JsonSerializer.Serialize(previous.Outputs));
            }
            previous = stopped;

            async Task Command(string command, int expectedResults)
            {
                using var content = new StringContent(command);
                using var response = await http.PostAsync(url, content, token);
                response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
                var status = await WaitFor(s => Messages(s, "controller.Output").Count() >= expectedResults
                    && Messages(s, "decode.Output").Count() >= expectedResults);
                var result = Messages(status, "controller.Output").Last();
                result.GetProperty("IsError").GetBoolean().ShouldBeFalse();
                var value = result.GetProperty("Value");
                value.GetProperty("Command").GetString().ShouldBe(command);
                value.GetProperty("Connected").GetBoolean().ShouldBe(command == "Connect");
                result.GetProperty("CausationId").GetString().ShouldBe(Messages(status, "decode.Output").Last().GetProperty("MessageId").GetString());
            }
            Task<string> Publish(string payload) => ReadContainer(container, token, "mosquitto_pub", "-u", "test", "-P", "trial", "-t", "trial/data", "-q", "1", "-m", payload);
            async Task<EnvironmentRunStatus> WaitFor(Func<EnvironmentRunStatus, bool> predicate)
            {
                while (true)
                {
                    var status = await runner.GetAsync(started.Id, token);
                    status.State.ShouldBe("Running", status.Error);
                    if (predicate(status)) return status;
                    await Task.Delay(25, token);
                }
            }
        }
        reopened.ToProjectJson().ShouldBe(saved);
        reopened.CanRedo.ShouldBeTrue();
        reopened.Redo();
        reopened.ToProjectJson().ShouldBe(withoutCommandWire);

        Task<string> TakeOver(string password) => ReadContainer(container, token, "mosquitto_pub", "-u", "test",
            "-P", password, "-i", "controller-trial", "-t", "trial/takeover", "-m", "disconnect");
        async Task<int> ConnectionAttempts() => (await ReadDockerOutput(token, "logs", container)).Split('\n')
            .Count(line => line.Contains("New connection from ", StringComparison.Ordinal));
        async Task SetPassword(string password)
        {
            await ReadContainer(container, token, "mosquitto_passwd", "-b", passwords, "test", password);
            await ReadDockerOutput(token, "kill", "--signal", "HUP", container);
        }
        static IEnumerable<JsonElement> Messages(EnvironmentRunStatus status, string port) =>
            status.Outputs!.Records.Where(r => r.Port == "main." + port).Select(r => r.Message!.Value);
        static IEnumerable<string?> Events(EnvironmentRunStatus status) =>
            Messages(status, "controller.Events").Select(m => m.GetProperty("Value").GetProperty("Name").GetString());
        static IEnumerable<string?> Deliveries(EnvironmentRunStatus status) =>
            Messages(status, "receive.Output").Select(m => m.GetProperty("Value").GetProperty("Text").GetString());
    }
}
