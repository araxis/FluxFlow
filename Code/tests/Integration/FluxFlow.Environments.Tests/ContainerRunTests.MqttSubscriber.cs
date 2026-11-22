using System.Text;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Components.Mqtt;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Mqtt;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed partial class ContainerRunTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Mosquitto_redelivers_both_pending_messages_after_out_of_order_feedback(bool rerun) =>
        CheckRedelivery(rerun, forwardWholeAck: null);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task Mosquitto_connection_cut_during_first_PUBACK_matches_broker_receipt(bool forwardWholeAck, bool rerun) =>
        CheckRedelivery(rerun, forwardWholeAck);

    private static async Task CheckRedelivery(bool rerun, bool? forwardWholeAck)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        await using var broker = await Broker(false).StartAsync(new()
        {
            Provider = "mosquitto",
            Files = new() { ["/mosquitto/config/mosquitto.conf"] =
                "listener 1883\nallow_anonymous true\nlog_dest stdout\nlog_type all\n" }
        }, token);
        await using var relay = forwardWholeAck is bool whole ? new MqttPubAckCut(broker.Endpoint, whole, token) : null;
        var port = relay?.Port ?? broker.Endpoint.Port;
        var config = """
            {"Resources":{"broker":{"Type":"mqtt.client","Host":"127.0.0.1","Port":PORT,"ClientId":"subscriber-trial",
              "AutoConnect":false,"CleanSession":false,"SessionExpiryInterval":120}},
             "Workflows":{"main":{"controller":{"Type":"mqtt.connection","Connection":"broker"},
               "receive":{"Type":"mqtt.subscriber","Connection":"broker","Topics":[{"Topic":"trial/jobs","QoS":1}],
                 "AcknowledgementMode":"Manual","MaxPendingAcknowledgements":2}}}}
            """.Replace("PORT", port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(config)));
        builder.Services.AddMqtt().AddFluxFlowApplication(builder.Configuration).AddFluxFlow();
        var host = builder.Build();
        await using var owner = (IAsyncDisposable)host;
        await host.StartAsync(token);
        var workflow = host.Services.GetRequiredKeyedService<FlowWorkflow>("main");
        try
        {
            var ports = Attach();
            await Command(MqttConnectionOperation.Connect);
            await Publish("one");
            await Publish("two");
            var first = await Delivery("one");
            var second = await Delivery("two");
            var brokerReceipts = forwardWholeAck == true ? 1 : 0;
            if (relay is null)
            {
                await Ack(second);
                // Rejection of the duplicate is a barrier on the same ACK input.
                await Ack(second);
                await ExpectEvent("mqtt.acknowledgement_rejected");
            }
            else
            {
                relay.Arm();
                await Ack(first);
                await relay.Intercepted.WaitAsync(token);
                (await ExpectEvent("mqtt.acknowledged")).Details!.Json.GetProperty("AcknowledgementId").GetString().ShouldBe(first);
                if (forwardWholeAck == true)
                    while (await BrokerAckCount() == 0) await Task.Delay(20, token);
                if (!rerun) relay.AllowRecovery();
                relay.Cut();
                (await ExpectEvent("mqtt.acknowledgements_abandoned")).Details!.Json.GetProperty("Count").GetInt32().ShouldBe(1);
            }
            (await BrokerAckCount()).ShouldBe(brokerReceipts);
            if (rerun)
            {
                await workflow.StopAsync().WaitAsync(token);
                if (relay is null)
                    (await ExpectEvent("mqtt.acknowledgements_abandoned")).Details!.Json.GetProperty("Count").GetInt32().ShouldBe(2);
                (await ports.Subscriber.ACK.SendAsync(FlowMessage.Create(new MqttAcknowledgementCommand { AcknowledgementId = first }), token)).ShouldBeFalse();
                await workflow.StartAsync().WaitAsync(token);
                ports = Attach();
            }
            else if (relay is null)
            {
                await Command(MqttConnectionOperation.Disconnect);
                (await ExpectEvent("mqtt.acknowledgements_abandoned")).Details!.Json.GetProperty("Count").GetInt32().ShouldBe(2);
            }
            if (relay is not null && !rerun)
                await ExpectEvent("mqtt.subscriptions_applied");
            else
            {
                relay?.AllowRecovery();
                await Command(MqttConnectionOperation.Connect);
            }
            var nextFirst = brokerReceipts == 0 ? await Delivery("one") : null;
            var nextSecond = await Delivery("two");
            var redelivered = nextFirst is null ? new[] { nextSecond } : new[] { nextFirst, nextSecond };
            redelivered.Concat([first, second]).Distinct().Count().ShouldBe(2 + redelivered.Length);
            foreach (var stale in new[] { first, second })
            {
                await Ack(stale);
                (await ExpectEvent("mqtt.acknowledgement_rejected")).Details!.Json.GetProperty("AcknowledgementId").GetString().ShouldBe(stale);
            }
            (await BrokerAckCount()).ShouldBe(brokerReceipts);
            await Ack(nextSecond);
            if (nextFirst is not null) await Ack(nextFirst);
            foreach (var id in redelivered)
                (await ExpectEvent("mqtt.acknowledged")).Details!.Json.GetProperty("AcknowledgementId").GetString().ShouldBe(id);
            await Command(MqttConnectionOperation.Disconnect);
            (await BrokerAckCount()).ShouldBe(2);
            await Command(MqttConnectionOperation.Connect);
            await Publish("fresh");
            await Ack(await Delivery("fresh"));
            await ExpectEvent("mqtt.acknowledged");
            await workflow.StopAsync().WaitAsync(token);
            ports.Output.Count.ShouldBe(0);

            async Task Command(MqttConnectionOperation operation)
            {
                var command = new MqttConnectionCommand { Command = operation, DisableReconnect = true };
                (await ports.Controller.Input.SendAsync(FlowMessage.Create(command), token)).ShouldBeTrue();
                var result = await ports.Commands.ReceiveAsync(token);
                result.IsError.ShouldBeFalse(result.Error?.Message);
            }

            async Task<string> Delivery(string expected)
            {
                var value = (await ports.Output.ReceiveAsync(token)).Value.Json;
                value.GetProperty("Text").GetString().ShouldBe(expected);
                return value.GetProperty("AcknowledgementId").GetString().ShouldNotBeNull();
            }

            async Task Ack(string id) =>
                (await ports.Subscriber.ACK.SendAsync(FlowMessage.Create(new MqttAcknowledgementCommand { AcknowledgementId = id }), token)).ShouldBeTrue();

            async Task<FlowEvent> ExpectEvent(string name)
            {
                while (true)
                {
                    var evt = (await ports.Events.ReceiveAsync(token)).Value.ShouldBeOfType<FlowEvent>();
                    if (evt.Name == name) return evt;
                    if (evt.Name is "mqtt.acknowledged" or "mqtt.acknowledgement_rejected" or "mqtt.acknowledgements_abandoned")
                        throw new InvalidOperationException($"Expected {name}, received {evt.Name}.");
                }
            }
        }
        finally { await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(15)); }

        Task<string> Publish(string payload) => ReadContainer(broker.ContainerId!, token,
            "mosquitto_pub", "-q", "1", "-t", "trial/jobs", "-m", payload);
        async Task<int> BrokerAckCount() => (await ReadDockerOutput(token, "logs", broker.ContainerId!))
            .Split('\n').Count(line => line.Contains("Received PUBACK from subscriber-trial ", StringComparison.Ordinal));

        (MqttConnection Controller, MqttSubscriber Subscriber, BufferBlock<FlowMessage> Commands,
            BufferBlock<FlowMessage> Output, BufferBlock<FlowMessage> Events) Attach()
        {
            var controller = (MqttConnection)workflow.Services.GetRequiredKeyedService<IFlowNode>("main.controller");
            var subscriber = (MqttSubscriber)workflow.Services.GetRequiredKeyedService<IFlowNode>("main.receive");
            return (controller, subscriber, Capture(controller.Output), Capture(subscriber.Output), Capture(subscriber.Events));
        }

        static BufferBlock<FlowMessage> Capture(ISourceBlock<FlowMessage> source)
        {
            var records = new BufferBlock<FlowMessage>();
            source.LinkTo(records);
            return records;
        }
    }
}
