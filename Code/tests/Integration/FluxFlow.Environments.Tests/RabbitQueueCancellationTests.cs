using System.Net;
using System.Text.Json;
using FluxFlow.Environments.Testcontainers;
using FluxFlow.Execution;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

[Collection("Containers")]
[Trait("Category", "Docker")]
public sealed class RabbitQueueCancellationTests
{
    [Fact]
    public async Task Deleting_an_active_queue_faults_the_run_and_old_feedback_cannot_settle_replacement_deliveries()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        const string queue = "queue-cancel-trial";
        const string cancellation = "The broker cancelled the AMQP consumer.";
        await using var broker = await new ConfiguredContainerProvider(FluxFlow.Plugins.RabbitMQ.RabbitMqAmqpContainer.Definition)
            .StartAsync(new() { Image = "rabbitmq:4.1.0-management" }, token);
        await using var admin = await new ConnectionFactory { Uri = broker.Endpoint }.CreateConnectionAsync(token);
        await using var channel = await admin.CreateChannelAsync(cancellationToken: token);
        using var logging = ExecutionLogTests.CreateFactory();
        using var plugins = RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.RabbitMQ.RabbitMQPlugin(), new FluxFlow.Plugins.Http.HttpPlugin());
        var preparation = new EnvironmentPreparation([new RabbitMqEnvironmentAdapter()], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        var request = new EnvironmentRunRequest("""
            {"Resources":{"broker":{"Type":"amqp.connection","Endpoint":"ENDPOINT"}},"Workflows":{"main":{
              "receive":{"Type":"amqp.consumer","Queue":"queue-cancel-trial","Connection":"broker","Prefetch":1,"RequeueOnNack":true,
                "ACK":"ack.Output","NACK":"nack.Output"},
              "accept":{"Type":"http.trigger","Route":"/ack","Output":"ack.Input"},
              "reject":{"Type":"http.trigger","Route":"/nack","Output":"nack.Input"},
              "ack":{"Type":"mapper","Engine":"jsonata","Expression":"{'SettlementToken':Body}"},
              "nack":{"Type":"mapper","Engine":"jsonata","Expression":"{'SettlementToken':Body}"}
            }}}
            """.Replace("ENDPOINT", broker.Endpoint.AbsoluteUri), new());

        await channel.QueueDeclareAsync(queue, true, false, false, cancellationToken: token);
        var first = Guid.NewGuid();
        string stale;
        try
        {
            var started = await runner.StartAsync(first, request, token);
            started.State.ShouldBe("Running", started.Error);
            await Publish(1);
            stale = Handle(await Delivery(first, 1, 1));
            await channel.QueueDeleteAsync(queue, ifUnused: false, ifEmpty: false, cancellationToken: token);
            var failed = await Wait(first, s => s.State == "Failed");
            failed.Error.ShouldNotBeNull().ShouldContain(cancellation);
            failed.Error.ShouldNotContain("AMQP consumer execution and cleanup failed.");
        }
        finally { await runner.StopAsync(first, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20)); }
        var stopped = await runner.GetAsync(first, token);
        stopped.State.ShouldBe("Stopped");
        stopped.Diagnostics!.Records.ShouldContain(r => r.Message == "Workflow execution failed." &&
            r.Exception != null && r.Exception.Contains(cancellation));
        var retained = JsonSerializer.Serialize(stopped.Outputs);

        // Reuse the queue name and the exact same workflow configuration.
        var recreated = await channel.QueueDeclareAsync(queue, true, false, false, cancellationToken: token);
        recreated.ConsumerCount.ShouldBe(0u);
        recreated.MessageCount.ShouldBe(0u);
        var next = Guid.NewGuid();
        using var http = new HttpClient();
        try
        {
            var started = await runner.StartAsync(next, request, token);
            started.State.ShouldBe("Running", started.Error);
            var url = HttpTriggerRunTests.ListenerAddresses(started).Single();
            await Publish(2);
            await Publish(3);
            var fresh = Handle(await Delivery(next, 1, 2));
            fresh.ShouldNotBe(stale);
            await Feedback("ack", stale);
            await Rejections(1, stale);
            await Feedback("nack", stale);
            await Rejections(2, stale);
            (await channel.QueueDeclarePassiveAsync(queue, token)).MessageCount.ShouldBe(1u);

            await Feedback("nack", fresh);
            var redelivered = await Delivery(next, 2, 2);
            redelivered.GetProperty("Redelivered").GetBoolean().ShouldBeTrue();
            Handle(redelivered).ShouldNotBe(fresh);
            await Feedback("ack", Handle(redelivered));
            var last = await Delivery(next, 3, 3);
            await Feedback("ack", Handle(last));
            await Feedback("ack", Handle(last));
            await Rejections(3, Handle(last)); // The duplicate is processed after the preceding ACK on the same input.

            async Task Feedback(string port, string handle)
            {
                using var content = new StringContent(handle);
                using var response = await http.PostAsync($"{url}/{port}", content, token);
                response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            }

            async Task Rejections(int count, string expectedToken)
            {
                var status = await Wait(next, s => Rejected(s).Length >= count);
                var rejected = Rejected(status);
                rejected.Length.ShouldBe(count);
                rejected[^1].GetProperty("Details").GetProperty("SettlementToken").GetString().ShouldBe(expectedToken);
            }
        }
        finally { await runner.StopAsync(next, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20)); }
        var drained = await channel.QueueDeclarePassiveAsync(queue, token);
        drained.ConsumerCount.ShouldBe(0u);
        drained.MessageCount.ShouldBe(0u);
        (await runner.GetAsync(next, token)).State.ShouldBe("Stopped");
        JsonSerializer.Serialize((await runner.GetAsync(first, token)).Outputs).ShouldBe(retained);

        async Task Publish(byte id) => await channel.BasicPublishAsync("", queue, new byte[] { id }, token);

        async Task<JsonElement> Delivery(Guid run, int count, byte id)
        {
            var status = await Wait(run, s => Values(s, "Output").Count() >= count);
            var records = Values(status, "Output").ToArray();
            records.Length.ShouldBe(count);
            records[^1].GetProperty("Body").GetBytesFromBase64().ShouldBe(new byte[] { id });
            return records[^1];
        }

        async Task<EnvironmentRunStatus> Wait(Guid run, Func<EnvironmentRunStatus, bool> done)
        {
            while (true)
            {
                var status = await runner.GetAsync(run, token);
                if (done(status)) return status;
                status.State.ShouldBe("Running", status.Error);
                await Task.Delay(25, token);
            }
        }

        static string Handle(JsonElement value) => value.GetProperty("SettlementToken").GetString().ShouldNotBeNull();
        static IEnumerable<JsonElement> Values(EnvironmentRunStatus status, string port) => status.Outputs!.Records
            .Where(r => r.Port == "main.receive." + port).Select(r => r.Message!.Value.GetProperty("Value"));
        static JsonElement[] Rejected(EnvironmentRunStatus status) => Values(status, "Events")
            .Where(e => e.GetProperty("Name").GetString() == "amqp.settlement_rejected").ToArray();
    }
}
