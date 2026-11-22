using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Components.RabbitMQ;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.RabbitMQ;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using FluxFlow.Environments.Testcontainers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

[Collection("Containers")]
[Trait("Category", "Docker")]
public sealed class RabbitPublisherOverflowTests
{
    [Fact]
    public async Task Partial_fanout_NACK_preserves_correlation_without_replaying_the_accepted_copy()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        await using var broker = await new ConfiguredContainerProvider(FluxFlow.Plugins.RabbitMQ.RabbitMqAmqpContainer.Definition)
            .StartAsync(new() { Image = "rabbitmq:4.1.0-management" }, token);
        await using var observer = await new ConnectionFactory { Uri = broker.Endpoint }.CreateConnectionAsync(token);
        await using var channel = await observer.CreateChannelAsync(cancellationToken: token);
        var configuration = """
            {"Resources":{"broker":{"Type":"amqp.connection","Endpoint":"ENDPOINT"}},"Workflows":{"main":{
              "publish":{"Type":"amqp.publisher","Connection":"broker","Topology":{
                "Exchanges":[{"Name":"events","Type":"fanout"}],
                "Queues":[{"Name":"limited","Arguments":{"x-max-length":{"Type":"Int32","Value":1},
                          "x-overflow":{"Type":"String","Value":"reject-publish"}}},{"Name":"audit"}],
                "Bindings":[{"Exchange":"events","Queue":"limited"},{"Exchange":"events","Queue":"audit"}]}}
            }}}
            """.Replace("ENDPOINT", broker.Endpoint.AbsoluteUri);
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration)));
        builder.Services.AddRabbitMQ().AddFluxFlowApplication(builder.Configuration).AddFluxFlow();
        var host = builder.Build();
        await using var owner = (IAsyncDisposable)host;
        await host.StartAsync(token);
        var workflow = host.Services.GetRequiredKeyedService<FlowWorkflow>("main");
        try
        {
            var (publisher, output) = Attach();
            await ExpectResult(output, await Send(publisher, 1), nack: false);
            await ExpectResult(output, await Send(publisher, 2), nack: true);
            await Drain("limited", 1);
            await Drain("audit", 1, 2);

            await ExpectResult(output, await Send(publisher, 3), nack: false);
            var last = await Send(publisher, 4);
            await workflow.StopAsync(token);
            await ExpectResult(output, last, nack: true);
            (await publisher.Input.SendAsync(last, token)).ShouldBeFalse();
            await workflow.StartAsync(token);
            var (next, nextOutput) = Attach();
            await Drain("limited", 3);
            await Drain("audit", 3, 4);

            await ExpectResult(nextOutput, await Send(next, 5), nack: false);
            await workflow.StopAsync(token);
            await Drain("limited", 5);
            await Drain("audit", 5);
            output.Count.ShouldBe(0);
            nextOutput.Count.ShouldBe(0);
        }
        finally { await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(20)); }

        (RabbitPublisher, BufferBlock<FlowMessage>) Attach()
        {
            var node = (RabbitPublisher)workflow.Services.GetRequiredKeyedService<IFlowNode>("main.publish");
            var output = new BufferBlock<FlowMessage>();
            node.Output.LinkTo(output);
            return (node, output);
        }

        async Task<FlowMessage> Send(RabbitPublisher node, byte id)
        {
            var request = new AmqpPublish
            {
                Exchange = "events", Body = Convert.ToBase64String([id]),
                Properties = new() { MessageId = $"event-{id}" }
            };
            var input = FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(request)), CorrelationId.New());
            (await node.Input.SendAsync(input, token)).ShouldBeTrue();
            return input;
        }

        async Task ExpectResult(BufferBlock<FlowMessage> output, FlowMessage input, bool nack)
        {
            var result = await output.ReceiveAsync(token);
            result.CausationId.ShouldBe(input.MessageId);
            result.CorrelationId.ShouldBe(input.CorrelationId);
            result.TraceId.ShouldBe(input.TraceId);
            if (nack)
            {
                var error = result.Error.ShouldNotBeNull();
                error.Code.ShouldBe("node.processing_failed");
                error.Details!.Json.GetProperty("exceptionType").GetString().ShouldBe(typeof(PublishException).FullName, error.Message);
            }
            else
            {
                result.Error.ShouldBeNull();
                result.Value.Json.GetProperty("Status").GetString().ShouldBe("Confirmed");
                result.Value.Json.GetProperty("MessageId").GetString().ShouldBe(input.Value.Json.GetProperty("Properties").GetProperty("MessageId").GetString());
            }
        }

        async Task Drain(string queue, params byte[] ids)
        {
            foreach (var id in ids)
            {
                BasicGetResult? message;
                while ((message = await channel.BasicGetAsync(queue, true, token)) is null) await Task.Delay(20, token);
                message.BasicProperties.MessageId.ShouldBe($"event-{id}");
                message.Body.ToArray().ShouldBe(new byte[] { id });
            }
            (await channel.BasicGetAsync(queue, true, token)).ShouldBeNull();
        }
    }
}
