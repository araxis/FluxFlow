using System.Text;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using FluxFlow.Application;
using FluxFlow.Components.RateLimit;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Mqtt;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Configuration.Components.RequestReply;
using FluxFlow.Expressions.Jsonata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet.Packets;
using MQTTnet.Server;
using MQTTnet;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed partial class MqttTests
{
    [Theory]
    [InlineData("ACK", " Manual ")]
    [InlineData("NACK", "01")]
    [InlineData("Timeout", "Manual")]
    public async Task Manual_ack_request_reply_wiring_acknowledges_only_success_and_stops_cleanly(string outcome, string mode)
    {
        await using var broker = await Broker.Start();
        var acknowledgements = ObserveAcknowledgements(broker);
        var json = $$"""
            {"Resources":{"broker":{"Type":"mqtt.client","Host":"127.0.0.1","Port":{{broker.Port}},"ClientId":"{{broker.ClientId}}"} },
            "Workflows":{"main":{
              "connection":{"Type":"mqtt.connection","Connection":"broker"},
              "receive":{"Type":"mqtt.subscriber","Connection":"broker","Topics":[{"Topic":"work","QoS":1}],
                "AcknowledgementMode":"{{mode}}","MaxPendingAcknowledgements":4,"Output":"exchange.Input","ACK":"settle.Output"},
              "exchange":{"Type":"requestReply","MaxInFlight":1,"ReplyTimeout":"00:00:00.3",
                "Output":{"Address":"settle.Input","Engine":"jsonata","Condition":"Outcome = 'ACK' and $exists(Request.AcknowledgementId)"} },
              "settle":{"Type":"mapper","Engine":"jsonata","Expression":"{'AcknowledgementId': Request.AcknowledgementId}"}
            } } }
            """;
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        await using var provider = new ServiceCollection().AddMqtt().AddMapper().AddRequestReply().AddJsonataExpressionEngine()
            .AddFluxFlowApplication(configuration).BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        var services = provider.GetWorkflowServices("main");
        var subscriber = services.GetRequiredKeyedService<IFlowNode>("main.receive").ShouldBeOfType<MqttSubscriber>();
        subscriber.TerminalInputs.ShouldContain(subscriber.ACK);
        var exchange = services.GetRequiredKeyedService<IFlowNode>("main.exchange").ShouldBeOfType<FluxFlow.Components.RateLimit.RateLimit>();
        var events = Capture(subscriber.Events);
        var requests = Capture(exchange.Request);
        var results = Capture(exchange.Output);
        await app.StartAsync();
        await WaitForEvent(events, "mqtt.subscriptions_applied");
        await broker.Publish("work", [0xff, 1]);
        var request = await requests.ReceiveAsync(Deadline);
        if (outcome != "Timeout")
            await (outcome == "ACK" ? exchange.Reply : exchange.Nack).SendAsync(request.WithValue((FlowValue)"handled"));
        var result = (await results.ReceiveAsync(Deadline)).Value.ShouldBeOfType<RequestReplyResult>();
        result.Outcome.ToString().ShouldBe(outcome);
        result.Request.Json.GetProperty("Payload").GetString().ShouldBe("/wE=");
        if (outcome == "ACK") (await acknowledgements.ReceiveAsync(Deadline)).ShouldBe("work");
        else await AssertNoAcknowledgements(acknowledgements);
        if (outcome == "Timeout")
        {
            await broker.Publish("work", [2]);
            (await results.ReceiveAsync(Deadline)).Value.ShouldBeOfType<RequestReplyResult>().Outcome.ShouldBe(RequestReplyOutcome.NotDispatched);
            await AssertNoAcknowledgements(acknowledgements);
        }
        await app.StopAsync().WaitAsync(Deadline);
        (await broker.Server.GetClientsAsync()).ShouldNotContain(client => client.Id == broker.ClientId);
    }

    [Theory]
    [InlineData("\"AcknowledgementMode\":\"Manual\",\"Topics\":\"work\"")]
    [InlineData("\"AcknowledgementMode\":\"Manual\",\"Topics\":{\"Topic\":\"work\",\"QoS\":2}")]
    [InlineData("\"AcknowledgementMode\":123")]
    [InlineData("\"MaxPendingAcknowledgements\":0")]
    public void Manual_ack_invalid_configuration_is_rejected_atomically(string options)
    {
        var json = $$"""{"Workflows":{"main":{"receive":{"Type":"mqtt.subscriber","Connection":"broker",{{options}} } } } }""";
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var services = new ServiceCollection().AddMqtt();
        var before = services.ToArray();
        Should.Throw<Exception>(() => services.AddFluxFlowApplication(configuration));
        services.ToArray().ShouldBe(before);
    }

    [Fact]
    public async Task Manual_ack_is_deferred_ordered_and_rejects_duplicate_feedback()
    {
        await using var broker = await Broker.Start();
        var acknowledgements = ObserveAcknowledgements(broker);
        await using var resource = broker.Resource();
        await using var connection = new MqttConnection(new() { Connection = resource });
        await using var subscriber = new MqttSubscriber(new()
        {
            Connection = resource, Topics = [new() { Topic = "manual/#", QoS = 1 }],
            AcknowledgementMode = MqttAcknowledgementMode.Manual, MaxPendingAcknowledgements = 2
        });
        var output = Capture(subscriber.Output);
        var events = Capture(subscriber.Events);
        await StartSubscription(connection, subscriber, events);
        await broker.Publish("manual/first", [0xff, 0x00]);
        var first = await output.ReceiveAsync(Deadline);
        first.Value.Json.GetProperty("Payload").GetString().ShouldBe("/wA=");
        first.Value.Json.GetProperty("Text").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        await broker.Publish("manual/second", Encoding.UTF8.GetBytes("second"));
        var second = await output.ReceiveAsync(Deadline);
        var secondFeedback = Acknowledge(second).WithHeaders(new Dictionary<string, FlowValue>
        {
            ["traceparent"] = "00-22222222222222222222222222222222-2222222222222222-01"
        });
        await subscriber.ACK.SendAsync(secondFeedback);
        await subscriber.ACK.SendAsync(Acknowledge(second));
        await WaitForEvent(events, "mqtt.acknowledgement_rejected");
        await AssertNoAcknowledgements(acknowledgements);
        var firstFeedback = Acknowledge(first).WithHeaders(new Dictionary<string, FlowValue>
        {
            ["traceparent"] = "00-11111111111111111111111111111111-1111111111111111-01"
        });
        await subscriber.ACK.SendAsync(firstFeedback);
        (await acknowledgements.ReceiveAsync(Deadline)).ShouldBe("manual/first");
        (await acknowledgements.ReceiveAsync(Deadline)).ShouldBe("manual/second");
        foreach (var feedback in new[] { firstFeedback, secondFeedback })
        {
            var acknowledged = await events.ReceiveAsync(Deadline);
            acknowledged.Value.ShouldBeOfType<FlowEvent>().Name.ShouldBe("mqtt.acknowledged");
            acknowledged.TraceId.ShouldBe(feedback.TraceId);
            acknowledged.CausationId.ShouldBe(feedback.MessageId);
            acknowledged.Headers["traceparent"].Json.ShouldBe(feedback.Headers["traceparent"].Json);
        }
        await subscriber.ACK.SendAsync(Acknowledge(first));
        await WaitForEvent(events, "mqtt.acknowledgement_rejected");
        await AssertNoAcknowledgements(acknowledgements);
        // Successful settlement releases the bounded pending slot.
        await broker.Publish("manual/third", [3]);
        await subscriber.ACK.SendAsync(Acknowledge(await output.ReceiveAsync(Deadline)));
        (await acknowledgements.ReceiveAsync(Deadline)).ShouldBe("manual/third");
    }

    [Theory]
    [InlineData(MqttAcknowledgementMode.Automatic)]
    [InlineData(MqttAcknowledgementMode.Manual)]
    public async Task Stopped_subscriber_does_not_acknowledge_late_deliveries(MqttAcknowledgementMode mode)
    {
        await using var broker = await Broker.Start();
        var acknowledgements = ObserveAcknowledgements(broker);
        var sent = new BufferBlock<string>();
        broker.Server.InterceptingOutboundPacketAsync += args =>
        {
            if (args.ClientId == broker.ClientId && args.Packet is MqttPublishPacket packet) sent.Post(packet.Topic);
            return Task.CompletedTask;
        };
        await using var resource = broker.Resource();
        await using var connection = new MqttConnection(new() { Connection = resource });
        await using var subscriber = new MqttSubscriber(new()
        {
            Connection = resource, Topics = [new() { Topic = "stop/#", QoS = 1 }], AcknowledgementMode = mode
        });
        var output = Capture(subscriber.Output);
        var events = Capture(subscriber.Events);
        await StartSubscription(connection, subscriber, events);
        await broker.Publish("stop/before", [1]);
        var before = await output.ReceiveAsync(Deadline);
        if (mode == MqttAcknowledgementMode.Automatic)
        {
            before.Value.Json.TryGetProperty("AcknowledgementId", out _).ShouldBeFalse();
            (await acknowledgements.ReceiveAsync(Deadline)).ShouldBe("stop/before");
        }
        subscriber.StopProduction();
        await subscriber.Completion.WaitAsync(Deadline);
        if (mode == MqttAcknowledgementMode.Manual)
            (await subscriber.ACK.SendAsync(Acknowledge(before))).ShouldBeFalse();
        resource.IsConnected.ShouldBeTrue();
        (await sent.ReceiveAsync(Deadline)).ShouldBe("stop/before");
        await broker.Publish("stop/after", [2]);
        (await sent.ReceiveAsync(Deadline)).ShouldBe("stop/after");
        await AssertNoAcknowledgements(acknowledgements);
        output.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Manual_ack_waiting_for_another_protocol_operation_does_not_delay_stop()
    {
        await using var broker = await Broker.Start();
        var acknowledgements = ObserveAcknowledgements(broker);
        var publishStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePublish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        broker.Server.InterceptingOutboundPacketAsync += async args =>
        {
            if (args.ClientId != broker.ClientId || args.Packet is not MqttPubAckPacket) return;
            publishStarted.TrySetResult();
            await releasePublish.Task.WaitAsync(Deadline);
        };
        await using var resource = broker.Resource(connectTimeout: TimeSpan.FromSeconds(30));
        await using var connection = new MqttConnection(new() { Connection = resource });
        await using var publisher = new MqttPublisher(new() { Connection = resource });
        await using var subscriber = new MqttSubscriber(new()
        {
            Connection = resource, Topics = [new() { Topic = "stop/gate", QoS = 1 }],
            AcknowledgementMode = MqttAcknowledgementMode.Manual
        });
        var output = Capture(subscriber.Output);
        var events = Capture(subscriber.Events);
        var published = Capture(publisher.Output);
        await StartSubscription(connection, subscriber, events);
        await broker.Publish("stop/gate", [1]);
        var delivery = await output.ReceiveAsync(Deadline);
        try
        {
            await publisher.Input.SendAsync(Message(new { Topic = "unrelated", Payload = "hold", QoS = 1 }));
            await publishStarted.Task.WaitAsync(Deadline);
            await subscriber.ACK.SendAsync(Acknowledge(delivery));
            await AssertNoAcknowledgements(acknowledgements);
            subscriber.StopProduction();
            await subscriber.Completion.WaitAsync(TimeSpan.FromSeconds(2));
            resource.IsConnected.ShouldBeTrue();
        }
        finally { releasePublish.TrySetResult(); }
        (await published.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        await AssertNoAcknowledgements(acknowledgements);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_ack_invalidates_old_ids_across_disconnect_and_redelivery(bool unexpected)
    {
        await using var broker = await Broker.Start(persistent: true);
        var acknowledgements = ObserveAcknowledgements(broker);
        await using var resource = broker.Resource(persistent: true, autoConnect: true, reconnectInterval: TimeSpan.FromMilliseconds(20));
        await using var connection = new MqttConnection(new() { Connection = resource });
        await using var subscriber = new MqttSubscriber(new()
        {
            Connection = resource, Topics = [new() { Topic = "reconnect", QoS = 1 }], AcknowledgementMode = MqttAcknowledgementMode.Manual
        });
        var output = Capture(subscriber.Output);
        var events = Capture(subscriber.Events);
        var commands = Capture(connection.Output);
        await StartSubscription(connection, subscriber, events);
        // StartSubscription's explicit Connect produces one command result.
        await commands.ReceiveAsync(Deadline);
        await broker.Publish("reconnect", [7]);
        var original = await output.ReceiveAsync(Deadline);
        if (unexpected)
            await (await broker.Server.GetClientsAsync()).Single(c => c.Id == broker.ClientId).DisconnectAsync();
        else
        {
            await connection.Input.SendAsync(FlowMessage.Create(new MqttConnectionCommand { Command = MqttConnectionOperation.Disconnect, DisableReconnect = true }));
            (await commands.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
            await connection.Input.SendAsync(FlowMessage.Create(new MqttConnectionCommand { Command = MqttConnectionOperation.Connect }));
            (await commands.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        }
        var redelivery = await output.ReceiveAsync(Deadline);
        redelivery.Value.Json.GetProperty("AcknowledgementId").GetString().ShouldNotBe(original.Value.Json.GetProperty("AcknowledgementId").GetString());
        await subscriber.ACK.SendAsync(Acknowledge(original));
        await WaitForEvent(events, "mqtt.acknowledgement_rejected");
        await AssertNoAcknowledgements(acknowledgements);
        await subscriber.ACK.SendAsync(Acknowledge(redelivery));
        (await acknowledgements.ReceiveAsync(Deadline)).ShouldBe("reconnect");
    }

    [Fact]
    public async Task Manual_ack_limit_faults_visibly_without_acknowledging_excess_deliveries()
    {
        await using var broker = await Broker.Start();
        var acknowledgements = ObserveAcknowledgements(broker);
        await using var resource = broker.Resource();
        await using var connection = new MqttConnection(new() { Connection = resource });
        await using var subscriber = new MqttSubscriber(new()
        {
            Connection = resource, Topics = [new() { Topic = "limit", QoS = 1 }],
            AcknowledgementMode = MqttAcknowledgementMode.Manual, MaxPendingAcknowledgements = 1
        });
        var output = Capture(subscriber.Output);
        var events = Capture(subscriber.Events);
        await StartSubscription(connection, subscriber, events);
        await broker.Publish("limit", [1]);
        await output.ReceiveAsync(Deadline);
        await broker.Publish("limit", [2]);
        var error = await Should.ThrowAsync<InvalidOperationException>(() => subscriber.Completion.WaitAsync(Deadline));
        error.Message.ShouldContain("pending acknowledgement limit");
        await AssertNoAcknowledgements(acknowledgements);
    }

    [Fact]
    public async Task Manual_ack_preserves_qos_zero_without_a_token_and_rejects_invalid_commands_and_qos_updates()
    {
        await using var broker = await Broker.Start();
        var acknowledgements = ObserveAcknowledgements(broker);
        await using var resource = broker.Resource();
        await using var connection = new MqttConnection(new() { Connection = resource });
        await using var subscriber = new MqttSubscriber(new()
        {
            Connection = resource, Topics = [new() { Topic = "mixed", QoS = 1 }], AcknowledgementMode = MqttAcknowledgementMode.Manual
        });
        var output = Capture(subscriber.Output);
        var events = Capture(subscriber.Events);
        await StartSubscription(connection, subscriber, events);
        await broker.Server.InjectApplicationMessage(new InjectedMqttApplicationMessage(
            new MqttApplicationMessageBuilder().WithTopic("mixed").WithPayload(new byte[] { 0xff }).Build()));
        var zero = await output.ReceiveAsync(Deadline);
        zero.Value.Json.GetProperty("QoS").GetInt32().ShouldBe(0);
        zero.Value.Json.GetProperty("Payload").GetString().ShouldBe("/w==");
        zero.Value.Json.TryGetProperty("AcknowledgementId", out _).ShouldBeFalse();
        foreach (var invalid in new[] { Message(new { }), Message(new { AcknowledgementId = "unknown" }) })
        {
            await subscriber.ACK.SendAsync(invalid);
            await WaitForEvent(events, "mqtt.acknowledgement_rejected");
        }
        await subscriber.Input.SendAsync(Message(new { Command = "Subscribe", Topics = new[] { new { Topic = "mixed", QoS = 2 } } }));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        await broker.Publish("mixed", [1]);
        await subscriber.ACK.SendAsync(Acknowledge(await output.ReceiveAsync(Deadline)));
        (await acknowledgements.ReceiveAsync(Deadline)).ShouldBe("mixed");
        subscriber.Completion.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Manual_ack_previous_subscriber_ids_cannot_settle_a_replacement_delivery()
    {
        await using var broker = await Broker.Start();
        var acknowledgements = ObserveAcknowledgements(broker);
        FlowMessage? previous = null;
        for (var run = 0; run < 2; run++)
        {
            await using var resource = broker.Resource();
            await using var connection = new MqttConnection(new() { Connection = resource });
            await using var subscriber = new MqttSubscriber(new()
            {
                Connection = resource, Topics = [new() { Topic = "rerun", QoS = 1 }], AcknowledgementMode = MqttAcknowledgementMode.Manual
            });
            var output = Capture(subscriber.Output);
            var events = Capture(subscriber.Events);
            await StartSubscription(connection, subscriber, events);
            await broker.Publish("rerun", [1]);
            var current = await output.ReceiveAsync(Deadline);
            if (previous is not null)
            {
                await subscriber.ACK.SendAsync(Acknowledge(previous));
                await WaitForEvent(events, "mqtt.acknowledgement_rejected");
                await AssertNoAcknowledgements(acknowledgements);
                await subscriber.ACK.SendAsync(Acknowledge(current));
                (await acknowledgements.ReceiveAsync(Deadline)).ShouldBe("rerun");
            }
            previous = current;
        }
    }

    private static FlowMessage Acknowledge(FlowMessage delivery) => delivery.WithValue(new MqttAcknowledgementCommand
    {
        AcknowledgementId = delivery.Value.Json.GetProperty("AcknowledgementId").GetString()!
    });

    private static BufferBlock<string> ObserveAcknowledgements(Broker broker)
    {
        var received = new BufferBlock<string>();
        broker.Server.ClientAcknowledgedPublishPacketAsync += args =>
        {
            if (args.ClientId == broker.ClientId) received.Post(args.PublishPacket.Topic);
            return Task.CompletedTask;
        };
        return received;
    }

    private static async Task AssertNoAcknowledgements(BufferBlock<string> acknowledgements)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Should.ThrowAsync<OperationCanceledException>(() => acknowledgements.ReceiveAsync(timeout.Token));
    }

    private static async Task StartSubscription(MqttConnection connection, MqttSubscriber subscriber, BufferBlock<FlowMessage> events)
    {
        await subscriber.StartAsync();
        await connection.StartAsync();
        await connection.Input.SendAsync(FlowMessage.Create(new MqttConnectionCommand { Command = MqttConnectionOperation.Connect }));
        await WaitForEvent(events, "mqtt.subscriptions_applied");
    }
}
