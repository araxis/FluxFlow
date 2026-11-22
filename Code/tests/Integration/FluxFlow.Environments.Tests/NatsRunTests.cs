using System.Text.Json;
using FluxFlow.Environments.Testcontainers;
using FluxFlow.Execution;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

[Collection("Containers")]
[Trait("Category", "Docker")]
public sealed class NatsRunTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Companion_delivery_retains_original_observations_after_cleanup_and_isolates_reruns(bool authenticated, bool jetStream)
    {
        using var plugins = RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.Nats.NatsPlugin());
        var services = new ServiceCollection().AddFlowEnvironments();
        foreach (var plugin in plugins.Modules) plugin.ConfigureHost(services);
        services.AddFlowContainers();
        using var provider = services.BuildServiceProvider();
        using var logging = ExecutionLogTests.CreateFactory();
        using var traces = new ExecutionTraceCollector();
        var execution = new WorkflowExecution(provider.GetRequiredService<EnvironmentPreparation>(), logging, traces, plugins: plugins);
        var request = new EnvironmentRunRequest(jetStream ? JetStreamConfiguration : Configuration, new()
        {
            Resources = new() { ["Resources:broker"] = new() { Provider = "nats", Image = "nats:2.11.8-alpine" } }
        });
        var tokenVariable = "NATS_TRIAL_" + Guid.NewGuid().ToString("N");
        try
        {
            if (authenticated)
            {
                Environment.SetEnvironmentVariable(tokenVariable, "trial-" + Guid.NewGuid().ToString("N"));
                request.Environment.Resources["Resources:broker"].Variables["NATS_TOKEN_ENVIRONMENT_VARIABLE"] = tokenVariable;
            }
            if (jetStream)
                request.Environment.Resources["Resources:broker"].Files["/etc/nats/nats.conf"] = "port: 4222\njetstream { store_dir: /tmp/jetstream }\n" +
                    (authenticated ? "authorization { token: $NATS_TOKEN }\n" : "");
            var original = JsonSerializer.Serialize(request);
            var first = await Run();
            var retainedLogs = JsonSerializer.Serialize(first.Logs.Snapshot());
            var retainedTraces = JsonSerializer.Serialize(first.Traces.Snapshot());
            var next = await Run();
            next.Logs.Snapshot().Records.ShouldNotBeEmpty();
            JsonSerializer.Serialize(first.Logs.Snapshot()).ShouldBe(retainedLogs);
            JsonSerializer.Serialize(first.Traces.Snapshot()).ShouldBe(retainedTraces);
            JsonSerializer.Serialize(request).ShouldBe(original);
        }
        finally { Environment.SetEnvironmentVariable(tokenVariable, null); }

        async Task<RunObservations> Run()
        {
            var observations = new RunObservations(Guid.NewGuid());
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            string[] containers;
            await using (var run = await execution.StartAsync(request, deadline.Token, observations))
            {
                containers = run.Services.Where(item => item.ContainerId != null).Select(item => item.ContainerId!).ToArray();
                var receivedBy = DateTime.UtcNow.AddSeconds(15);
                var expected = jetStream ? "JetStream delivery AP8B" : "NATS response AP8B";
                while (observations.Logs.Snapshot().Records.Count(item => item.Message == expected) < 5 && DateTime.UtcNow < receivedBy)
                    await Task.Delay(25, deadline.Token);
                observations.Logs.Snapshot().Records.Count(item => item.Message == expected).ShouldBeGreaterThanOrEqualTo(5,
                    JsonSerializer.Serialize(observations.Outputs.Snapshot()));
                observations.Outputs.Snapshot().Records.ShouldContain(item => item.Port == (jetStream ? "main.consumer.Output" : "main.receive.Output") &&
                    (jetStream ? item.Message!.Value.GetProperty("Value").GetProperty("Message") : item.Message!.Value.GetProperty("Value")).GetProperty("DataBase64").GetString() == "AP8B");
                if (jetStream)
                    observations.Outputs.Snapshot().Records.ShouldContain(item => item.Port == "main.acknowledge.Output" &&
                        item.Message!.Value.GetProperty("Value").GetProperty("Action").GetString() == "Ack");
            }
            containers.ShouldHaveSingleItem();
            foreach (var container in containers) await ContainerRunTests.AssertRemoved(container);
            observations.Logs.Snapshot().Records.Where(item => item.Message == (jetStream ? "JetStream delivery AP8B" : "NATS response AP8B"))
                .ShouldAllBe(item => item.Level == "Information" && item.Category == "main.record" && item.Exception == null);
            observations.Logs.Snapshot().Records.ShouldContain(item => item.Category == "NATS.Client.Core.NatsConnection" &&
                item.Message.StartsWith("Disposing connection", StringComparison.Ordinal) && item.Level == "Information");
            observations.Traces.Snapshot().Records.ShouldNotBeEmpty();
            var stopped = JsonSerializer.Serialize(observations.Outputs.Snapshot());
            await Task.Delay(150);
            JsonSerializer.Serialize(observations.Outputs.Snapshot()).ShouldBe(stopped);
            return observations;
        }
    }
    [Fact]
    public async Task Invalid_startup_file_preserves_the_broker_error_and_removes_the_container()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var binding = new ResourceBinding { Provider = "nats", Image = "nats:2.11.8-alpine",
            Files = new() { ["/etc/nats/nats.conf"] = "unknown_startup_setting: true\n" } };
        var error = await Should.ThrowAsync<DotNet.Testcontainers.Containers.ContainerNotRunningException>(() => new ConfiguredContainerProvider(FluxFlow.Plugins.Nats.NatsContainer.Definition).StartAsync(binding, deadline.Token));
        error.ToString().ShouldContain("unknown_startup_setting");
        // Testcontainers includes the failed container ID in its diagnostic. Other target
        // frameworks may be running their own managed NATS containers concurrently.
        var container = System.Text.RegularExpressions.Regex.Match(error.Message, @"^Container ([a-f0-9]{64}) exited");
        container.Success.ShouldBeTrue(error.Message);
        await ContainerRunTests.AssertRemoved(container.Groups[1].Value);
    }

    public const string Configuration = """
        {"Resources":{"broker":{"Type":"nats.connection","Endpoint":"nats://localhost:4222"}},"Workflows":{"main":{
          "tick":{"Type":"scheduler","Interval":"00:00:00.2","Output":"publication.Input"},
          "publication":{"Type":"mapper","Engine":"jsonata","Expression":"{\"Subject\":\"service.echo\",\"DataBase64\":\"AP8B\",\"Headers\":{\"Correlation-Id\":[$messageId]}}","Output":"request.Input"},
          "request":{"Type":"nats.request","Connection":"broker","TimeoutSeconds":2,"Output":"format.Input"},
          "receive":{"Type":"nats.subscriber","Connection":"broker","Subject":"service.echo","Capacity":4,"Output":"reply.Input"},
          "reply":{"Type":"nats.reply","Connection":"broker"},
          "format":{"Type":"mapper","Engine":"jsonata","Expression":"{\"MessageTemplate\":\"NATS response {Bytes}\",\"LogLevel\":\"Information\",\"Args\":[DataBase64]}","Output":"record.Input"},
          "record":{"Type":"logger"}
        }}}
        """;
    public const string JetStreamConfiguration = """
        {"Resources":{"broker":{"Type":"nats.connection","Endpoint":"nats://localhost:4222"}},"Workflows":{"main":{
          "tick":{"Type":"scheduler","Interval":"00:00:00.2","Output":"publication.Input"},
          "publication":{"Type":"mapper","Engine":"jsonata","Expression":"{\"Subject\":\"events.one\",\"DataBase64\":\"AP8B\",\"Headers\":{\"Nats-Msg-Id\":[$messageId]}}","Output":"publisher.Input"},
          "publisher":{"Type":"nats.jetstream.publisher","Connection":"broker","Stream":"TRIAL","CreateStream":true},
          "consumer":{"Type":"nats.jetstream.consumer","Connection":"broker","Stream":"TRIAL","CreateStream":true,"Consumer":"worker","CreateConsumer":true,"MaxInFlight":2,"Capacity":2,"Output":"format.Input"},
          "format":{"Type":"mapper","Engine":"jsonata","Expression":"{\"MessageTemplate\":\"JetStream delivery {Bytes}\",\"LogLevel\":\"Information\",\"Args\":[Message.DataBase64],\"Attributes\":{\"SettlementToken\":SettlementToken}}","Output":"record.Input"},
          "record":{"Type":"logger","Output":"settlement.Input"},
          "settlement":{"Type":"mapper","Engine":"jsonata","Expression":"{\"SettlementToken\":Attributes.SettlementToken,\"Action\":\"Ack\"}","Output":"acknowledge.Input"},
          "acknowledge":{"Type":"nats.jetstream.settlement","Consumer":"consumer"}
        }}}
        """;
}
