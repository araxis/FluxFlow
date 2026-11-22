using FluxFlow.Execution.Plugins;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Environments.Testcontainers;
using FluxFlow.Execution;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

[Collection("Containers")]
[Trait("Category", "Docker")]
public sealed class RabbitRequestReplyRunTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_reply_settles_the_original_delivery_and_retains_timeouts_across_stop(bool requeue)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var broker = await new ConfiguredContainerProvider(FluxFlow.Plugins.RabbitMQ.RabbitMqAmqpContainer.Definition).StartAsync(
            new() { Image = "rabbitmq:4.1.0-management" }, deadline.Token);
        await using var connection = await new ConnectionFactory { Uri = broker.Endpoint }.CreateConnectionAsync(deadline.Token);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: deadline.Token);
        using var catalog = RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.RabbitMQ.RabbitMQPlugin());
        using var provider = new ServiceCollection().AddLocalFlowRunner(catalog).BuildServiceProvider();
        using var logging = ExecutionLogTests.CreateFactory();
        using var traces = new ExecutionTraceCollector();
        var execution = new WorkflowExecution(provider.GetRequiredService<EnvironmentPreparation>(), logging, traces, plugins: catalog);
        var configuration = JsonNode.Parse(Configuration)!;
        configuration["Resources"]!["broker"]!["Endpoint"] = broker.Endpoint.AbsoluteUri;
        var workflow = configuration["Workflows"]!["main"]!;
        workflow["consumer"]!["RequeueOnNack"] = requeue;
        var request = new EnvironmentRunRequest(configuration.ToJsonString(), new());
        var original = JsonSerializer.Serialize(request);
        var first = new RunObservations(Guid.NewGuid());
        var resultCount = requeue ? 5 : 4;

        await using (var run = await execution.StartAsync(request, deadline.Token, first))
        {
            await Until(() => Values(first, "publisher.Output").Length == 5 &&
                Values(first, "reply.Output").Length == resultCount, deadline.Token);
            var results = Values(first, "reply.Output");
            results.Count(item => item.GetProperty("Outcome").GetString() == "ACK").ShouldBe(requeue ? 2 : 1);
            results.Count(item => item.GetProperty("Outcome").GetString() == "NACK").ShouldBe(1);
            results.Count(item => item.GetProperty("Outcome").GetString() == "Timeout").ShouldBe(2);
            var deliveries = Values(first, "consumer.Output");
            deliveries.Length.ShouldBe(resultCount);
            deliveries.Count(item => item.GetProperty("Redelivered").GetBoolean()).ShouldBe(requeue ? 1 : 0);
            foreach (var result in results)
            {
                var originalDelivery = result.GetProperty("Request");
                var input = first.Outputs.Snapshot().Records.Single(item => item.Port == "main.consumer.Output" &&
                    item.Message!.Value.GetProperty("MessageId").GetString() == result.GetProperty("RequestId").GetString());
                input.Message!.Value.GetProperty("Value").GetRawText().ShouldBe(originalDelivery.GetRawText());
                originalDelivery.GetProperty("Properties").GetProperty("CorrelationId").GetString().ShouldBe("shared-business-id");
                if (result.GetProperty("Outcome").GetString() == "Timeout")
                    result.GetProperty("Reply").ValueKind.ShouldBe(JsonValueKind.Null);
                else
                    result.GetProperty("Reply").GetRawText().ShouldBe(originalDelivery.GetRawText());
            }
            results.Select(item => item.GetProperty("RequestId").GetString()).Distinct().Count().ShouldBe(resultCount);
            Values(first, "publisher.Output").ShouldAllBe(item => item.GetProperty("Status").GetString() == "Confirmed");
            // Two timed-out deliveries still occupy prefetch. The fifth publication remains ready.
            (await channel.QueueDeclarePassiveAsync("request-reply-trial", deadline.Token)).MessageCount.ShouldBe(1u);
            await Task.Delay(200, deadline.Token);
            Values(first, "consumer.Output").Length.ShouldBe(resultCount);
        }

        var stoppedQueue = await channel.QueueDeclarePassiveAsync("request-reply-trial", deadline.Token);
        stoppedQueue.ConsumerCount.ShouldBe(0u);
        stoppedQueue.MessageCount.ShouldBe(3u);
        first.Traces.Snapshot().Records.ShouldNotBeEmpty();
        first.Logs.Snapshot().Records.Where(item => item.Category == "main.record")
            .Count(item => item.Message.StartsWith("Request outcome ", StringComparison.Ordinal)).ShouldBe(resultCount);
        var retainedOutputs = JsonSerializer.Serialize(first.Outputs.Snapshot());
        var retainedLogs = JsonSerializer.Serialize(first.Logs.Snapshot());
        var retainedTraces = JsonSerializer.Serialize(first.Traces.Snapshot());
        var oldDeliveries = Values(first, "reply.Output")
            .Where(item => item.GetProperty("Outcome").GetString() == "Timeout")
            .Select(item => item.GetProperty("Request")).ToArray();

        // A second workflow run drains the same broker queue; it does not publish again.
        workflow["samples"]!["Values"] = "[]";
        workflow["reply"]!["Request"] = "success.Input";
        workflow["stale"] = JsonSerializer.SerializeToNode(new
        {
            Type = "source", Values = JsonSerializer.Serialize(oldDeliveries),
            Input = "tick.Output", Output = "consumer.ACK"
        });
        var second = new RunObservations(Guid.NewGuid());
        await using (var run = await execution.StartAsync(new(configuration.ToJsonString(), new()), deadline.Token, second))
        {
            await Until(() => Values(second, "reply.Output").Length == 3 &&
                Values(second, "consumer.Events").Count(item => item.GetProperty("Name").GetString() == "amqp.settlement_rejected") == 2,
                deadline.Token);
            Values(second, "reply.Output").ShouldAllBe(item => item.GetProperty("Outcome").GetString() == "ACK");
            var deliveries = Values(second, "consumer.Output");
            deliveries.Select(item => item.GetProperty("Body").GetString()).Order().ShouldBe(new[] { "Aw==", "BA==", "BQ==" });
            deliveries.ShouldAllBe(item => !oldDeliveries.Any(old =>
                old.GetProperty("SettlementToken").GetString() == item.GetProperty("SettlementToken").GetString()));
            deliveries.Where(item => item.GetProperty("Body").GetString() != "BQ==")
                .ShouldAllBe(item => item.GetProperty("Redelivered").GetBoolean());
        }
        var drainedQueue = await channel.QueueDeclarePassiveAsync("request-reply-trial", deadline.Token);
        drainedQueue.ConsumerCount.ShouldBe(0u);
        drainedQueue.MessageCount.ShouldBe(0u);
        JsonSerializer.Serialize(first.Outputs.Snapshot()).ShouldBe(retainedOutputs);
        JsonSerializer.Serialize(first.Logs.Snapshot()).ShouldBe(retainedLogs);
        JsonSerializer.Serialize(first.Traces.Snapshot()).ShouldBe(retainedTraces);
        JsonSerializer.Serialize(request).ShouldBe(original);
    }

    private static JsonElement[] Values(RunObservations observations, string port) => observations.Outputs.Snapshot().Records
        .Where(item => item.Port == "main." + port && item.Message.HasValue)
        .Select(item => item.Message!.Value.GetProperty("Value")).ToArray();

    private static async Task Until(Func<bool> completed, CancellationToken cancellationToken)
    {
        while (!completed()) await Task.Delay(25, cancellationToken);
    }

    private const string Configuration = """
        {"Resources":{"broker":{"Type":"amqp.connection","Endpoint":"amqp://localhost:5672"}},"Workflows":{"main":{
          "tick":{"Type":"scheduler","Interval":"00:00:00.1","Output":"samples.Input"},
          "samples":{"Type":"source","Values":"[\"AQ==\",\"Ag==\",\"Aw==\",\"BA==\",\"BQ==\"]","Output":"publication.Input"},
          "publication":{"Type":"mapper","Engine":"jsonata","Expression":"{'RoutingKey':'request-reply-trial','Body':$,'Properties':{'CorrelationId':'shared-business-id'}}","Output":"publisher.Input"},
          "publisher":{"Type":"amqp.publisher","Connection":"broker","Topology":{"Queues":[{"Name":"request-reply-trial"}]}},
          "consumer":{"Type":"amqp.consumer","Connection":"broker","Queue":"request-reply-trial","Prefetch":2,"Capacity":2,
            "Topology":{"Queues":[{"Name":"request-reply-trial"}]},"Output":"reply.Input","ACK":"acknowledge.Output","NACK":"reject.Output"},
          "reply":{"Type":"requestReply","MaxInFlight":2,"BufferCapacity":2,"ReplyTimeout":"00:00:01","TimeoutBehavior":"Continue",
            "Request":[{"Address":"success.Input","Engine":"jsonata","Condition":"Body = 'AQ==' or (Body = 'Ag==' and Redelivered)"},
                       {"Address":"failure.Input","Engine":"jsonata","Condition":"Body = 'Ag==' and $not(Redelivered)"}],
            "ACK":"success.Output","NACK":"failure.Output",
            "Output":[{"Address":"acknowledge.Input","Engine":"jsonata","Condition":"Outcome = 'ACK'"},
                      {"Address":"reject.Input","Engine":"jsonata","Condition":"Outcome = 'NACK'"},"format.Input"]},
          "success":{"Type":"mapper","Engine":"jsonata","Expression":"$"},
          "failure":{"Type":"mapper","Engine":"jsonata","Expression":"$"},
          "acknowledge":{"Type":"mapper","Engine":"jsonata","Expression":"Request"},
          "reject":{"Type":"mapper","Engine":"jsonata","Expression":"Request"},
          "format":{"Type":"mapper","Engine":"jsonata","Expression":"{'MessageTemplate':'Request outcome {Outcome}','LogLevel':'Information','Args':[Outcome]}","Output":"record.Input"},
          "record":{"Type":"logger"}
        }}}
        """;
}
