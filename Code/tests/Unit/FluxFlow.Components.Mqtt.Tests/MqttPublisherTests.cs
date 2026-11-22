using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Mqtt;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Exceptions;
using MQTTnet.Packets;
using MQTTnet.Server;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed partial class MqttTests
{
    public enum PubAckScenario { Delayed, Lost, Disconnect, Stop }

    [Theory]
    [InlineData(PubAckScenario.Delayed)]
    [InlineData(PubAckScenario.Lost)]
    [InlineData(PubAckScenario.Disconnect)]
    [InlineData(PubAckScenario.Stop)]
    public async Task Publisher_completes_each_QoS1_request_once(PubAckScenario scenario)
    {
        await using var broker = await Broker.Start(persistent: true);
        var publications = new ConcurrentQueue<string>();
        var firstAck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        broker.Server.InterceptingPublishAsync += args =>
        {
            if (args.ClientId == broker.ClientId) publications.Enqueue(args.ApplicationMessage.ConvertPayloadToString());
            return Task.CompletedTask;
        };
        broker.Server.InterceptingOutboundPacketAsync += async args =>
        {
            if (args.ClientId != broker.ClientId || args.Packet is not MqttPubAckPacket || !firstAck.TrySetResult()) return;
            if (scenario == PubAckScenario.Delayed) await releaseAck.Task.WaitAsync(Deadline);
            else args.ProcessPacket = false;
        };

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Resources:broker:Type"] = "mqtt.client",
            ["Resources:broker:Address"] = $"mqtt://127.0.0.1:{broker.Port}",
            ["Resources:broker:ClientId"] = broker.ClientId,
            ["Resources:broker:CleanSession"] = "false",
            ["Resources:broker:SessionExpiryInterval"] = "60",
            ["Resources:broker:AutoConnect"] = "false",
            ["Resources:broker:Timeout"] = scenario == PubAckScenario.Lost ? "00:00:02" : "00:00:10",
            ["Workflows:main:controller:Type"] = "mqtt.connection",
            ["Workflows:main:controller:Connection"] = "broker",
            ["Workflows:main:send:Type"] = "mqtt.publisher",
            ["Workflows:main:send:Connection"] = "broker"
        });
        builder.Services.AddMqtt().AddFluxFlowApplication(builder.Configuration).AddFluxFlow();
        var host = builder.Build();
        await using var owner = (IAsyncDisposable)host;
        await host.StartAsync().WaitAsync(Deadline);
        var workflow = host.Services.GetRequiredKeyedService<FlowWorkflow>("main");
        try
        {
            var (publisher, output, events) = await Connect();
            var first = await Send(publisher, "first");
            await firstAck.Task.WaitAsync(Deadline);
            publications.ToArray().ShouldBe(["first"]);
            output.Count.ShouldBe(0);

            var stopping = Task.CompletedTask;
            switch (scenario)
            {
                case PubAckScenario.Delayed:
                    await Assert.ThrowsAsync<TimeoutException>(() => output.ReceiveAsync(TimeSpan.FromMilliseconds(150)));
                    releaseAck.SetResult();
                    break;
                case PubAckScenario.Disconnect:
                    await (await broker.Server.GetClientsAsync()).Single(c => c.Id == broker.ClientId).DisconnectAsync();
                    await WaitForEvent(events, "mqtt.connected");
                    break;
                case PubAckScenario.Stop: stopping = workflow.StopAsync(); break;
            }
            var result = await output.ReceiveAsync(Deadline);
            AssertPublisherResult(result, first, error: scenario != PubAckScenario.Delayed);
            if (scenario == PubAckScenario.Lost)
                result.Error!.Details!.Json.GetProperty("exceptionType").GetString().ShouldBe(typeof(MqttCommunicationTimedOutException).FullName);
            if (scenario == PubAckScenario.Disconnect)
            {
                var recovered = await Send(publisher, "reconnected");
                AssertPublisherResult(await output.ReceiveAsync(Deadline), recovered, error: false);
            }
            if (scenario != PubAckScenario.Stop) stopping = workflow.StopAsync();
            await stopping.WaitAsync(Deadline);
            publisher.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            output.Count.ShouldBe(0);

            await workflow.StartAsync().WaitAsync(Deadline);
            var (nextPublisher, nextOutput, _) = await Connect();
            var next = await Send(nextPublisher, "next");
            AssertPublisherResult(await nextOutput.ReceiveAsync(Deadline), next, error: false);
            await workflow.StopAsync().WaitAsync(Deadline);
            nextOutput.Count.ShouldBe(0);
            publications.ToArray().ShouldBe(scenario == PubAckScenario.Disconnect
                ? ["first", "reconnected", "next"] : ["first", "next"]);
        }
        finally
        {
            releaseAck.TrySetResult();
            await host.StopAsync().WaitAsync(Deadline + Deadline);
        }

        async Task<(MqttPublisher, BufferBlock<FlowMessage>, BufferBlock<FlowMessage>)> Connect()
        {
            var services = workflow.Services;
            var controller = (MqttConnection)services.GetRequiredKeyedService<IFlowNode>("main.controller");
            var events = Capture(controller.Events);
            var commands = Capture(controller.Output);
            await controller.Input.SendAsync(FlowMessage.Create(new MqttConnectionCommand { Command = MqttConnectionOperation.Connect }));
            (await commands.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
            await WaitForEvent(events, "mqtt.connected");
            var publisher = (MqttPublisher)services.GetRequiredKeyedService<IFlowNode>("main.send");
            return (publisher, Capture(publisher.Output), events);
        }

        static async Task<FlowMessage> Send(MqttPublisher publisher, string payload)
        {
            var message = FlowMessage.Create(new MqttPublishRequest { Topic = "trial/data", Payload = payload, QoS = 1 },
                correlationId: CorrelationId.New());
            (await publisher.Input.SendAsync(message).WaitAsync(Deadline)).ShouldBeTrue();
            return message;
        }
    }

    private static void AssertPublisherResult(FlowMessage result, FlowMessage request, bool error)
    {
        result.IsError.ShouldBe(error, result.Error?.Message);
        result.CorrelationId.ShouldBe(request.CorrelationId);
        result.CausationId.ShouldBe(request.MessageId);
        result.TraceId.ShouldBe(request.TraceId);
        if (error) result.Error!.Code.ShouldBe("node.processing_failed");
        else
        {
            result.Value.Json.GetProperty("Topic").GetString().ShouldBe("trial/data");
            result.Value.Json.GetProperty("ReasonCode").GetString().ShouldBeOneOf("Success", "NoMatchingSubscribers");
        }
    }
}
