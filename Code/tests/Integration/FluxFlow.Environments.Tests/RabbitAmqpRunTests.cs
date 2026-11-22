using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Environments.Testcontainers;
using FluxFlow.Execution;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

[Collection("Containers")]
[Trait("Category", "Docker")]
public sealed class RabbitAmqpRunTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Companion_delivers_and_settles_repeatedly_with_retained_logs_and_isolated_reruns(bool authenticated, bool feedback)
    {
        using var plugins = RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.RabbitMQ.RabbitMQPlugin());
        var services = new ServiceCollection().AddFlowEnvironments();
        foreach (var plugin in plugins.Modules) plugin.ConfigureHost(services);
        services.AddFlowContainers();
        using var provider = services.BuildServiceProvider();
        using var logging = ExecutionLogTests.CreateFactory();
        using var traces = new ExecutionTraceCollector();
        var execution = new WorkflowExecution(provider.GetRequiredService<EnvironmentPreparation>(), logging, traces, plugins: plugins);
        var project = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rabbitmq.fluxflow"));
        var configuration = JsonNode.Parse(project)!["Configuration"]!;
        if (feedback)
        {
            var workflow = configuration["Workflows"]!["main"]!.AsObject();
            workflow["consumer"]!["ACK"] = "settlement.Output";
            workflow["settlement"]!.AsObject().Remove("Output");
            workflow.Remove("acknowledge");
        }
        var request = new EnvironmentRunRequest(configuration.ToJsonString(), new()
        {
            Resources = new() { ["Resources:broker"] = new() { Provider = "rabbitmq-amqp", Image = "rabbitmq:4.1.0-management" } }
        });
        var userVariable = "AMQP_TRIAL_USER_" + Guid.NewGuid().ToString("N");
        var passwordVariable = "AMQP_TRIAL_PASSWORD_" + Guid.NewGuid().ToString("N");
        try
        {
            if (authenticated)
            {
                Environment.SetEnvironmentVariable(userVariable, "trial");
                Environment.SetEnvironmentVariable(passwordVariable, Guid.NewGuid().ToString("N"));
                request.Environment.Resources["Resources:broker"].Variables = new()
                {
                    ["RABBITMQ_DEFAULT_USER_ENVIRONMENT_VARIABLE"] = userVariable,
                    ["RABBITMQ_DEFAULT_PASS_ENVIRONMENT_VARIABLE"] = passwordVariable,
                    ["RABBITMQ_DEFAULT_VHOST"] = "trial"
                };
            }
            var original = JsonSerializer.Serialize(request);
            var first = await Run();
            var retained = JsonSerializer.Serialize(first.Logs.Snapshot());
            var next = await Run();
            next.Logs.Snapshot().Records.ShouldNotBeEmpty();
            JsonSerializer.Serialize(first.Logs.Snapshot()).ShouldBe(retained);
            JsonSerializer.Serialize(request).ShouldBe(original);
        }
        finally
        {
            Environment.SetEnvironmentVariable(userVariable, null);
            Environment.SetEnvironmentVariable(passwordVariable, null);
        }

        async Task<RunObservations> Run()
        {
            var observations = new RunObservations(Guid.NewGuid());
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            string[] containers;
            await using (var run = await execution.StartAsync(request, deadline.Token, observations))
            {
                containers = run.Services.Where(item => item.ContainerId != null).Select(item => item.ContainerId!).ToArray();
                var receivedBy = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < receivedBy &&
                    (observations.Logs.Snapshot().Records.Count(item => item.Message == "AMQP delivery AP8B") < 5 ||
                     !feedback && observations.Outputs.Snapshot().Records.Count(item => item.Port == "main.acknowledge.Output") < 5))
                    await Task.Delay(25, deadline.Token);
                observations.Logs.Snapshot().Records.Count(item => item.Message == "AMQP delivery AP8B").ShouldBeGreaterThanOrEqualTo(5,
                    JsonSerializer.Serialize(observations.Outputs.Snapshot()));
                observations.Outputs.Snapshot().Records.ShouldContain(item => item.Port == "main.consumer.Output" &&
                    item.Message!.Value.GetProperty("Value").GetProperty("Body").GetString() == "AP8B");
            }
            containers.ShouldNotBeEmpty();
            foreach (var container in containers) await ContainerRunTests.AssertRemoved(container);
            if (!feedback)
            {
                var records = observations.Outputs.Snapshot().Records;
                var commands = records.Where(r => r.Port == "main.settlement.Output")
                    .Select(r => r.Message!.Value.GetRawText()).ToHashSet(StringComparer.Ordinal);
                var completed = records.Where(r => r.Port == "main.acknowledge.Output" && !r.Message!.Value.GetProperty("IsError").GetBoolean())
                    .Select(r => r.Message!.Value.GetRawText()).ToArray();
                completed.Length.ShouldBeGreaterThanOrEqualTo(5);
                completed.ShouldAllBe(result => commands.Contains(result));
            }
            observations.Logs.Snapshot().Records.Where(item => item.Message == "AMQP delivery AP8B")
                .ShouldAllBe(item => item.Level == "Information" && item.Category == "main.record" && item.Exception == null);
            observations.Traces.Snapshot().Records.ShouldNotBeEmpty();
            var stopped = JsonSerializer.Serialize(observations.Outputs.Snapshot());
            await Task.Delay(150);
            JsonSerializer.Serialize(observations.Outputs.Snapshot()).ShouldBe(stopped);
            return observations;
        }
    }
}
