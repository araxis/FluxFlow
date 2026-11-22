using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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
using MQTTnet.Server;
using Polly.Retry;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed partial class MqttTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_client_survives_publisher_or_subscriber_workflow_restarts(bool restartSubscriber)
    {
        await using var broker = await Broker.Start();
        var connections = 0;
        var subscriptions = 0;
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publications = new BufferBlock<string>();
        broker.Server.ValidatingConnectionAsync += _ =>
        {
            Interlocked.Increment(ref connections);
            return Task.CompletedTask;
        };
        broker.Server.InterceptingSubscriptionAsync += _ =>
        {
            Interlocked.Increment(ref subscriptions);
            return Task.CompletedTask;
        };
        broker.Server.InterceptingPublishAsync += args =>
        {
            if (args.ClientId == broker.ClientId)
                publications.Post(args.ApplicationMessage.ConvertPayloadToString());
            return Task.CompletedTask;
        };
        broker.Server.ClientDisconnectedAsync += args =>
        {
            if (args.ClientId == broker.ClientId) disconnected.TrySetResult();
            return Task.CompletedTask;
        };

        var subscriberWorkflow = restartSubscriber ? "worker" : "transport";
        var publisherWorkflow = restartSubscriber ? "transport" : "worker";
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Resources:broker:Type"] = "mqtt.client",
            ["Resources:broker:Address"] = $"mqtt://127.0.0.1:{broker.Port}",
            ["Resources:broker:ClientId"] = broker.ClientId,
            ["Resources:broker:AutoConnect"] = "true",
            ["Workflows:transport:connection:Type"] = "mqtt.connection",
            ["Workflows:transport:connection:Connection"] = "broker",
            [$"Workflows:{subscriberWorkflow}:receive:Type"] = "mqtt.subscriber",
            [$"Workflows:{subscriberWorkflow}:receive:Connection"] = "broker",
            [$"Workflows:{subscriberWorkflow}:receive:Topics"] = "shared/readings",
            [$"Workflows:{publisherWorkflow}:send:Type"] = "mqtt.publisher",
            [$"Workflows:{publisherWorkflow}:send:Connection"] = "broker"
        });
        builder.Services.AddMqtt().AddFluxFlowApplication(builder.Configuration).AddFluxFlow();
        var host = builder.Build();
        await using var owner = (IAsyncDisposable)host;
        var resource = host.Services.GetRequiredFlowResource<MqttClientResource>("broker");
        try
        {
            await host.StartAsync().WaitAsync(Deadline);
            var worker = host.Services.GetRequiredKeyedService<FlowWorkflow>("worker");
            var transport = host.Services.GetRequiredKeyedService<FlowWorkflow>("transport");
            var connection = transport.Services.GetRequiredKeyedService<IFlowNode>("transport.connection");
            var subscriber = GetSubscriber();
            var publisher = GetPublisher();
            var received = Capture(subscriber.Output);
            var replies = Capture(publisher.Output);
            await WaitForEvent(Capture(subscriber.Events), "mqtt.subscriptions_applied");
            await AssertDelivery("initial");

            for (var cycle = 1; cycle <= 3; cycle++)
            {
                IFlowNode previous = restartSubscriber ? subscriber : publisher;
                await worker.StopAsync().WaitAsync(Deadline);
                previous.Completion.IsCompletedSuccessfully.ShouldBeTrue();
                transport.Completion.IsCompleted.ShouldBeFalse();
                resource.IsConnected.ShouldBeTrue();
                received.Count.ShouldBe(0);
                replies.Count.ShouldBe(0);
                await AssertDelivery($"stopped-{cycle}", workerStopped: true);

                await worker.StartAsync().WaitAsync(Deadline);
                if (restartSubscriber)
                {
                    subscriber = GetSubscriber();
                    subscriber.ShouldNotBeSameAs(previous);
                    publisher.ShouldBeSameAs(GetPublisher());
                    received = Capture(subscriber.Output);
                    await WaitForEvent(Capture(subscriber.Events), "mqtt.subscriptions_applied");
                }
                else
                {
                    publisher = GetPublisher();
                    publisher.ShouldNotBeSameAs(previous);
                    subscriber.ShouldBeSameAs(GetSubscriber());
                    replies = Capture(publisher.Output);
                }
                transport.Services.GetRequiredKeyedService<IFlowNode>("transport.connection").ShouldBeSameAs(connection);
                worker.Services.GetRequiredFlowResource<MqttClientResource>("broker").ShouldBeSameAs(resource);
                await AssertDelivery($"restarted-{cycle}");
                Volatile.Read(ref subscriptions).ShouldBe(restartSubscriber ? cycle + 1 : 1);
            }

            await host.StopAsync().WaitAsync(Deadline);
            worker.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            transport.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            received.Count.ShouldBe(0);
            replies.Count.ShouldBe(0);
            publications.Count.ShouldBe(0);

            async Task AssertDelivery(string payload, bool workerStopped = false)
            {
                if (workerStopped && !restartSubscriber)
                    await broker.Publish("shared/readings", Encoding.UTF8.GetBytes(payload));
                else
                {
                    (await publisher.Input.SendAsync(FlowMessage.Create(new MqttPublishRequest
                    {
                        Topic = "shared/readings", Payload = payload, QoS = 1
                    })).WaitAsync(Deadline)).ShouldBeTrue();
                    var reply = await replies.ReceiveAsync(Deadline);
                    reply.IsError.ShouldBeFalse(reply.Error?.Message);
                    (await publications.ReceiveAsync(Deadline)).ShouldBe(payload);
                }
                if (!workerStopped || !restartSubscriber)
                {
                    var message = await received.ReceiveAsync(Deadline);
                    message.IsError.ShouldBeFalse();
                    message.Value.Json.GetProperty("Text").GetString().ShouldBe(payload);
                }
            }
        }
        finally { await host.StopAsync().WaitAsync(Deadline); }
        await disconnected.Task.WaitAsync(Deadline);
        Volatile.Read(ref connections).ShouldBe(1);
        resource.IsConnected.ShouldBeFalse();
        (await broker.Server.GetClientsAsync()).ShouldNotContain(client => client.Id == broker.ClientId);

        MqttSubscriber GetSubscriber() => (MqttSubscriber)host.Services.GetWorkflowServices(subscriberWorkflow)
            .GetRequiredKeyedService<IFlowNode>($"{subscriberWorkflow}.receive");
        MqttPublisher GetPublisher() => (MqttPublisher)host.Services.GetWorkflowServices(publisherWorkflow)
            .GetRequiredKeyedService<IFlowNode>($"{publisherWorkflow}.send");
    }

    [Fact]
    public async Task Subscriber_can_be_replaced_while_the_shared_connection_stays_running()
    {
        await using var broker = await Broker.Start();
        await using var resource = broker.Resource(persistent: true);
        await using var connection = new MqttConnection(new() { Connection = resource });
        var first = new MqttSubscriber(new() { Connection = resource, Topics = [new() { Topic = "old" }] });
        var replies = Capture(connection.Output);
        await connection.StartAsync();
        await first.StartAsync();
        await connection.Input.SendAsync(FlowMessage.Create(new MqttConnectionCommand { Command = MqttConnectionOperation.Connect }));
        (await replies.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        await first.DisposeAsync();
        resource.IsConnected.ShouldBeTrue();
        await using var second = new MqttSubscriber(new() { Connection = resource, Topics = [new() { Topic = "new" }] });
        var events = Capture(second.Events);
        var received = Capture(second.Output);
        await second.StartAsync();
        await WaitForEvent(events, "mqtt.subscriptions_applied");
        await broker.Publish("old", Encoding.UTF8.GetBytes("obsolete"));
        await broker.Publish("new", Encoding.UTF8.GetBytes("current"));
        (await received.ReceiveAsync(Deadline)).Value.Json.GetProperty("Topic").GetString().ShouldBe("new");
        first.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public void Typed_commands_preserve_declared_property_names_including_QoS()
    {
        var publish = new MqttPublishRequest { Topic = "sensor/value", Payload = "{\"MixedCase\":1,\"lower\":2}", QoS = 1 };
        publish.Json.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["Topic", "Payload", "Encoding", "QoS", "Retain"]);
        publish.Json.GetProperty("Payload").GetString().ShouldBe(publish.Payload);
        publish.Json.GetProperty("QoS").GetInt32().ShouldBe(1);

        var subscription = new MqttSubscriptionCommand
        {
            Command = MqttSubscriptionOperation.Subscribe,
            Topics = [new() { Topic = "sensor/+", QoS = 2 }]
        };
        subscription.Json.GetProperty("Topics")[0].EnumerateObject().Select(property => property.Name)
            .ShouldBe(["Topic", "QoS"]);
        subscription.Json.GetProperty("Topics")[0].GetProperty("QoS").GetInt32().ShouldBe(2);
        var connection = new MqttConnectionCommand { Command = MqttConnectionOperation.Disconnect, DisableReconnect = true };
        connection.Json.EnumerateObject().Select(property => property.Name).ShouldBe(["Command", "DisableReconnect"]);
    }

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static FlowMessage Message(object value) => FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(value)));
    private static BufferBlock<FlowMessage> Capture(ISourceBlock<FlowMessage> output)
    {
        var capture = new BufferBlock<FlowMessage>();
        output.LinkTo(capture);
        return capture;
    }

    [Fact]
    public async Task Retained_binary_and_utf8_messages_publish_results_and_lineage()
    {
        await using var broker = await Broker.Start();
        await broker.Publish("factory/retained", [0, 255, 128], retain: true);
        await using var resource = broker.Resource();
        await using var connection = new MqttConnection(new() { Connection = resource });
        await using var subscriber = new MqttSubscriber(new()
        {
            Connection = resource, Topics = [new() { Topic = "factory/#", QoS = 1 }]
        });
        await using var publisher = new MqttPublisher(new() { Connection = resource });
        var connected = Capture(connection.Output);
        var received = Capture(subscriber.Output);
        var published = Capture(publisher.Output);
        await subscriber.StartAsync();
        await connection.StartAsync();
        var connect = FlowMessage.Create(new MqttConnectionCommand { Command = MqttConnectionOperation.Connect });
        await connection.Input.SendAsync(connect);
        var result = await connected.ReceiveAsync(Deadline);
        result.IsError.ShouldBeFalse(result.Error?.Message);
        result.TraceId.ShouldBe(connect.TraceId);
        result.CausationId.ShouldBe(connect.MessageId);
        var retained = await received.ReceiveAsync(Deadline);
        retained.Value.Json.GetProperty("Payload").GetString().ShouldBe("AP+A");
        retained.Value.Json.GetProperty("Text").ValueKind.ShouldBe(JsonValueKind.Null);
        retained.Value.Json.GetProperty("Retain").GetBoolean().ShouldBeTrue();
        retained.TraceId.ShouldNotBe(connect.TraceId);

        var request = Message(new { topic = "factory/data", payload = "hello ö", qos = 2 });
        await publisher.Input.SendAsync(request);
        var sent = await published.ReceiveAsync(Deadline);
        sent.IsError.ShouldBeFalse(sent.Error?.Message);
        sent.TraceId.ShouldBe(request.TraceId);
        var data = (await received.ReceiveAsync(Deadline)).Value.Json;
        data.GetProperty("Text").GetString().ShouldBe("hello ö");
        data.GetProperty("QoS").GetInt32().ShouldBe(1);
        Convert.FromBase64String(data.GetProperty("Payload").GetString()!).ShouldBe(Encoding.UTF8.GetBytes("hello ö"));

        await publisher.Input.SendAsync(FlowMessage.Create(new MqttPublishRequest
        {
            Topic = "factory/binary", Payload = "AP+A", Encoding = MqttPayloadEncoding.Base64
        }));
        (await published.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        (await received.ReceiveAsync(Deadline)).Value.Json.GetProperty("Payload").GetString().ShouldBe("AP+A");
    }

    [Fact]
    public async Task Runtime_topics_survive_reconnection_and_explicit_disconnect_suspends_reconnect()
    {
        await using var broker = await Broker.Start();
        await using var resource = broker.Resource(persistent: true, reconnectInterval: TimeSpan.FromMilliseconds(50));
        await using var connection = new MqttConnection(new()
        {
            Connection = resource
        });
        await using var subscriber = new MqttSubscriber(new() { Connection = resource, Topics = [new() { Topic = "old", QoS = 1 }] });
        var replies = Capture(connection.Output);
        var received = Capture(subscriber.Output);
        var events = Capture(subscriber.Events);
        await subscriber.StartAsync();
        await connection.StartAsync();
        await Command("Connect");
        await Event("mqtt.subscriptions_applied");
        await Command("Disconnect");
        await Task.Delay(200);
        resource.IsConnected.ShouldBeFalse();
        await subscriber.Input.SendAsync(Message(new { command = "Unsubscribe", topics = new[] { new { topic = "old" } } }));
        await Event("mqtt.subscriptions_pending");
        await subscriber.Input.SendAsync(Message(new { command = "Subscribe", topics = new[] { new { topic = "new", qos = 1 } } }));
        await Event("mqtt.subscriptions_pending");
        await Command("Connect");
        await Event("mqtt.subscriptions_applied");
        await AssertCurrentTopics();
        var client = (await broker.Server.GetClientsAsync()).Single(client => client.Id == broker.ClientId);
        await client.DisconnectAsync();
        await Event("mqtt.subscriptions_applied");
        resource.IsConnected.ShouldBeTrue();
        await AssertCurrentTopics();

        async Task Command(string command)
        {
            await connection.Input.SendAsync(Message(new { command }));
            var reply = await replies.ReceiveAsync(Deadline);
            reply.IsError.ShouldBeFalse(reply.Error?.Message);
        }
        async Task Event(string name)
        {
            using var timeout = new CancellationTokenSource(Deadline);
            while ((await events.ReceiveAsync(timeout.Token)).Value.Json.GetProperty("Name").GetString() != name) { }
        }
        async Task AssertCurrentTopics()
        {
            await broker.Publish("old", [1]);
            await broker.Publish("new", [2]);
            (await received.ReceiveAsync(Deadline)).Value.Json.GetProperty("Topic").GetString().ShouldBe("new");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Should.ThrowAsync<OperationCanceledException>(() => received.ReceiveAsync(timeout.Token));
        }
    }

    [Theory]
    [InlineData("\"factory/#\"")]
    [InlineData("{\"Topic\":\"factory/#\",\"QoS\":1}")]
    [InlineData("[\"factory/one\",{\"Topic\":\"factory/two\",\"QoS\":2}]")]
    public async Task Configuration_builds_one_shared_client_without_connecting_until_start(string topics)
    {
        await using var broker = await Broker.Start();
        var json = $$"""
        { "Resources": { "mqtt": { "Type": "mqtt.client", "Host": "127.0.0.1", "Port": {{broker.Port}}, "ClientId": "{{broker.ClientId}}" } },
          "Workflows": { "demo": {
            "connection": { "Type": "mqtt.connection", "Connection": "mqtt" },
            "subscriber": { "Type": "mqtt.subscriber", "Connection": "mqtt", "Topics": {{topics}} },
            "publisher": { "Type": "mqtt.publisher", "Connection": "mqtt" }
          } } }
        """;
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var services = new ServiceCollection().AddMqtt();
        services.AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        var resource = provider.GetRequiredFlowResource<MqttClientResource>("mqtt");
        resource.IsConnected.ShouldBeFalse();
        var events = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.subscriber.Events"));
        var received = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.subscriber.Output"));
        await app.StartAsync();
        (await events.ReceiveAsync(Deadline)).Value.Json.GetProperty("Name").GetString().ShouldBe("mqtt.subscriptions_applied");
        await broker.Publish("factory/one", [3]);
        (await received.ReceiveAsync(Deadline)).Value.Json.GetProperty("Payload").GetString().ShouldBe("Aw==");
        await app.StopAsync().WaitAsync(Deadline);
        resource.IsConnected.ShouldBeFalse();
    }

    [Theory]
    [InlineData("{\"Topic\":\"x\",\"QoS\":3}")]
    [InlineData("{\"Topic\":\"x/#/y\"}")]
    [InlineData("{\"Topic\":\"x\",\"Typo\":1}")]
    [InlineData("\"\"")]
    public void Invalid_subscription_configuration_is_located_and_atomic(string topics)
    {
        var json = """{"Workflows":{"test":{"sub":{"Type":"mqtt.subscriber","Connection":"mqtt","Topics": """ + topics + "}}}}";
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var services = new ServiceCollection().AddMqtt();
        var before = services.ToArray();
        var error = Should.Throw<Exception>(() => services.AddFluxFlowApplication(config));
        error.ToString().ShouldContain("Topics");
        services.ToArray().ShouldBe(before);
    }

    [Fact]
    public async Task Live_subscription_updates_do_not_replay_unrelated_retained_messages()
    {
        await using var broker = await Broker.Start();
        await broker.Publish("retained", [1], retain: true);
        await using var resource = broker.Resource();
        await using var connection = new MqttConnection(new() { Connection = resource });
        await using var subscriber = new MqttSubscriber(new() { Connection = resource, Topics = [new() { Topic = "retained" }] });
        var replies = Capture(connection.Output);
        var received = Capture(subscriber.Output);
        var events = Capture(subscriber.Events);
        await connection.Input.SendAsync(Message(new { command = "Connect" }));
        (await replies.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        (await received.ReceiveAsync(Deadline)).Value.Json.GetProperty("Topic").GetString().ShouldBe("retained");
        await events.ReceiveAsync(Deadline);
        await subscriber.Input.SendAsync(FlowMessage.Create(new MqttSubscriptionCommand
        {
            Command = MqttSubscriptionOperation.Subscribe, Topics = [new() { Topic = "extra", QoS = 1 }]
        }));
        await events.ReceiveAsync(Deadline);
        await broker.Publish("extra", [2]);
        (await received.ReceiveAsync(Deadline)).Value.Json.GetProperty("Topic").GetString().ShouldBe("extra");
        await subscriber.Input.SendAsync(Message(new { command = "Unsubscribe", topics = new[] { new { topic = "extra" } } }));
        await events.ReceiveAsync(Deadline);
        await broker.Publish("extra", [3]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Should.ThrowAsync<OperationCanceledException>(() => received.ReceiveAsync(timeout.Token));
    }

    [Fact]
    public async Task Connection_refusal_is_an_error_and_a_later_command_can_recover()
    {
        await using var broker = await Broker.Start();
        var reject = true;
        broker.Server.ValidatingConnectionAsync += args =>
        {
            if (reject) args.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.NotAuthorized;
            return Task.CompletedTask;
        };
        await using var resource = broker.Resource();
        await using var connection = new MqttConnection(new() { Connection = resource });
        var replies = Capture(connection.Output);
        await connection.Input.SendAsync(Message(new { command = "Connect" }));
        (await replies.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        resource.IsConnected.ShouldBeFalse();
        reject = false;
        await connection.Input.SendAsync(Message(new { command = "Connect" }));
        (await replies.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        resource.IsConnected.ShouldBeTrue();
    }

    [Fact]
    public async Task Rejected_subscription_can_be_removed_and_retry_does_not_restore_it()
    {
        await using var broker = await Broker.Start();
        broker.Server.InterceptingSubscriptionAsync += args =>
        {
            if (args.TopicFilter.Topic == "denied")
                args.Response.ReasonCode = MQTTnet.Protocol.MqttSubscribeReasonCode.NotAuthorized;
            return Task.CompletedTask;
        };
        await using var resource = broker.Resource();
        await using var connection = new MqttConnection(new() { Connection = resource });
        await using var subscriber = new MqttSubscriber(new() { Connection = resource });
        var replies = Capture(connection.Output);
        var output = Capture(subscriber.Output);
        var events = Capture(subscriber.Events);
        await connection.Input.SendAsync(Message(new { command = "Connect" }));
        (await replies.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        await events.ReceiveAsync(Deadline);
        await subscriber.Input.SendAsync(Message(new { command = "Subscribe", topics = new[] { new { topic = "denied" } } }));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        await subscriber.Input.SendAsync(Message(new { command = "Unsubscribe", topics = new[] { new { topic = "denied" } } }));
        await events.ReceiveAsync(Deadline);
        await subscriber.Input.SendAsync(Message(new { command = "Subscribe", topics = new[] { new { topic = "allowed" } } }));
        await events.ReceiveAsync(Deadline);
        await broker.Publish("allowed", [4]);
        (await output.ReceiveAsync(Deadline)).Value.Json.GetProperty("Topic").GetString().ShouldBe("allowed");
    }

    [Fact]
    public async Task Failed_automatic_connection_can_stop_without_waiting_for_the_next_retry()
    {
        await using var broker = await Broker.Start();
        await using var resource = broker.Resource(autoConnect: true, reconnectInterval: TimeSpan.FromMinutes(5));
        await broker.Server.StopAsync();
        await using var connection = new MqttConnection(new()
        {
            Connection = resource
        });
        var errors = Capture(connection.Output);
        await connection.StartAsync();
        (await errors.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        connection.StopProduction();
        await connection.Completion.WaitAsync(Deadline);
        resource.IsConnected.ShouldBeFalse();
    }

    [Fact]
    public async Task Invalid_requests_emit_errors_and_single_resource_ownership_is_enforced()
    {
        await using var broker = await Broker.Start();
        await using var resource = broker.Resource();
        await using var connection = new MqttConnection(new() { Connection = resource });
        await using var subscriber = new MqttSubscriber(new() { Connection = resource });
        await using var publisher = new MqttPublisher(new() { Connection = resource });
        Should.Throw<InvalidOperationException>(() => new MqttConnection(new() { Connection = resource }));
        Should.Throw<InvalidOperationException>(() => new MqttSubscriber(new() { Connection = resource }));
        var results = Capture(publisher.Output);
        var upstream = FlowMessage.Create(new FlowError("test", "test", "test"));
        await publisher.Input.SendAsync(upstream);
        (await results.ReceiveAsync(Deadline)).ShouldBeSameAs(upstream);
        foreach (var request in new object[]
        {
            new { topic = "invalid/#", payload = "test" }, new { topic = "x", payload = "test", qos = 4 },
            new { topic = "x", payload = "%%%", encoding = "Base64" }, new { topic = "x", payload = "offline" }
        })
        {
            await publisher.Input.SendAsync(Message(request));
            (await results.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Resource_controls_auto_connect_and_connection_reports_state_transitions(bool autoConnect)
    {
        await using var broker = await Broker.Start();
        var json = $$"""
        { "Resources": { "mqtt": { "Type": "mqtt.client", "Host": "127.0.0.1",
            "Port": {{broker.Port}}, "ClientId": "{{broker.ClientId}}",
            "AutoConnect": {{autoConnect.ToString().ToLowerInvariant()}}, "Reconnect": { "Delay": "00:00:00.025" } } },
          "Workflows": { "demo": { "connection": { "Type": "mqtt.connection", "Connection": "mqtt" } } } }
        """;
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var services = new ServiceCollection().AddMqtt();
        services.AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        var resource = provider.GetRequiredFlowResource<MqttClientResource>("mqtt");
        var input = provider.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.connection.Input");
        var output = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.connection.Output"));
        var events = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.connection.Events"));
        resource.IsConnected.ShouldBeFalse();
        await app.StartAsync();
        if (!autoConnect)
        {
            await Task.Delay(150);
            resource.IsConnected.ShouldBeFalse();
            (await broker.Server.GetClientsAsync()).ShouldBeEmpty();
            events.TryReceive(out _).ShouldBeFalse();
            await input.SendAsync(Message(new { command = "Connect" }));
            (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        }
        (await events.ReceiveAsync(Deadline)).Value.Json.GetProperty("Name").GetString().ShouldBe("mqtt.connecting");
        (await events.ReceiveAsync(Deadline)).Value.Json.GetProperty("Name").GetString().ShouldBe("mqtt.connected");
        resource.IsConnected.ShouldBeTrue();
        await input.SendAsync(Message(new { command = "Disconnect", disableReconnect = true }));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        (await events.ReceiveAsync(Deadline)).Value.Json.GetProperty("Name").GetString().ShouldBe("mqtt.disconnecting");
        (await events.ReceiveAsync(Deadline)).Value.Json.GetProperty("Name").GetString().ShouldBe("mqtt.disconnected");
        await Task.Delay(150);
        resource.IsConnected.ShouldBeFalse();
        events.TryReceive(out _).ShouldBeFalse();
        await app.StopAsync().WaitAsync(Deadline);
    }

    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 1)]
    [InlineData(false, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 2)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 2)]
    public async Task Disconnect_honors_auto_connect_and_optional_suppression(bool autoConnect, bool disableReconnect, int representation)
    {
        await using var broker = await Broker.Start();
        var attempts = 0;
        broker.Server.ValidatingConnectionAsync += _ => { Interlocked.Increment(ref attempts); return Task.CompletedTask; };
        await using var resource = broker.Resource(autoConnect: autoConnect, reconnectInterval: TimeSpan.FromMilliseconds(25));
        await using var connection = new MqttConnection(new() { Connection = resource });
        var output = Capture(connection.Output);
        var events = Capture(connection.Events);
        await connection.StartAsync();
        if (!autoConnect)
        {
            await connection.Input.SendAsync(Message(new { command = "Connect" }));
            (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        }
        await WaitForEvent(events, "mqtt.connected");
        var command = new MqttConnectionCommand { Command = MqttConnectionOperation.Disconnect, DisableReconnect = disableReconnect };
        var request = representation switch
        {
            0 => FlowMessage.Create(command),
            1 => FlowMessage.Create(new JsonFlowValue(command.Json)),
            _ => disableReconnect
                ? Message(new { command = "Disconnect", disableReconnect = true })
                : Message(new { command = "Disconnect" })
        };
        await connection.Input.SendAsync(request);
        var result = await output.ReceiveAsync(Deadline);
        result.IsError.ShouldBeFalse();
        result.Value.Json.GetProperty("Connected").GetBoolean().ShouldBeFalse();
        result.CausationId.ShouldBe(request.MessageId);
        if (autoConnect && !disableReconnect)
            await WaitForEvent(events, "mqtt.connected");
        else
        {
            await Task.Delay(150);
            resource.IsConnected.ShouldBeFalse();
            Volatile.Read(ref attempts).ShouldBe(1);
            await connection.Input.SendAsync(Message(new { command = "Connect" }));
            (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        }
        resource.IsConnected.ShouldBeTrue();
        Volatile.Read(ref attempts).ShouldBe(2);
        connection.StopProduction();
        await connection.ProductionCompletion.WaitAsync(Deadline);
        connection.Input.Complete();
        await connection.Completion.WaitAsync(Deadline);
        await Task.Delay(100);
        resource.IsConnected.ShouldBeFalse();
        Volatile.Read(ref attempts).ShouldBe(2);
    }

    [Theory]
    [InlineData("AutoConnect", "false")]
    [InlineData("ReconnectInterval", "\"00:00:05\"")]
    [InlineData("Reconnect", "{\"MaxRetryAttempts\":3}")]
    public void Resource_settings_on_connection_component_are_rejected(string setting, string value)
    {
        var json = $$"""
        { "Workflows": { "demo": { "connection": { "Type": "mqtt.connection", "Connection": "mqtt", "{{setting}}": {{value}} } } } }
        """;
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var services = new ServiceCollection().AddMqtt();
        var before = services.ToArray();
        var error = Should.Throw<Exception>(() => services.AddFluxFlowApplication(config));
        error.ToString().ShouldContain($"Workflows:demo:connection:{setting}");
        error.ToString().ShouldContain("mqtt.client resource");
        services.ToArray().ShouldBe(before);
    }

    [Fact]
    public void Removed_resource_reconnect_interval_is_rejected_during_registration()
    {
        const string json = """
        { "Resources": { "mqtt": { "Type": "mqtt.client", "Host": "localhost", "ClientId": "test", "ReconnectInterval": "00:00:00" } },
          "Workflows": { "demo": { "connection": { "Type": "mqtt.connection", "Connection": "mqtt" } } } }
        """;
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var services = new ServiceCollection().AddMqtt();
        var before = services.ToArray();
        Should.Throw<Exception>(() => services.AddFluxFlowApplication(config)).ToString().ShouldContain("ReconnectInterval");
        services.ToArray().ShouldBe(before);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Configured_retry_budget_exhausts_once_and_resets_after_recovery(int retries)
    {
        await using var broker = await Broker.Start();
        var accept = 0;
        var attempts = 0;
        broker.Server.ValidatingConnectionAsync += args =>
        {
            Interlocked.Increment(ref attempts);
            if (Volatile.Read(ref accept) == 0) args.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.ServerUnavailable;
            return Task.CompletedTask;
        };
        var json = $$"""
        { "Resources": { "mqtt": { "Type": "mqtt.client", "Host": "127.0.0.1", "Port": {{broker.Port}},
          "ClientId": "{{broker.ClientId}}", "Reconnect": { "MaxRetryAttempts": {{retries}}, "Delay": "00:00:00.020" } } },
          "Workflows": { "demo": { "connection": { "Type": "mqtt.connection", "Connection": "mqtt" } } } }
        """;
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var services = new ServiceCollection().AddMqtt();
        services.AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        var events = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.connection.Events"));
        var output = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.connection.Output"));
        var input = provider.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.connection.Input");
        await app.StartAsync();
        await WaitForEvent(events, "mqtt.reconnect_exhausted");
        for (var attempt = 0; attempt <= retries; attempt++) (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        await Task.Delay(150);
        Volatile.Read(ref attempts).ShouldBe(retries + 1);
        Volatile.Write(ref accept, 1);
        await input.SendAsync(Message(new { command = "Connect" }));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        Volatile.Read(ref attempts).ShouldBe(retries + 2);
        Volatile.Write(ref accept, 0);
        await (await broker.Server.GetClientsAsync()).Single().DisconnectAsync();
        await WaitForEvent(events, "mqtt.reconnect_exhausted");
        await Task.Delay(150);
        Volatile.Read(ref attempts).ShouldBe(2 * (retries + 1) + 1);
        await app.StopAsync().WaitAsync(Deadline);
    }

    [Fact]
    public async Task Retry_uses_native_exponential_backoff_and_maximum_delay()
    {
        await using var broker = await Broker.Start();
        var attempts = 0;
        broker.Server.ValidatingConnectionAsync += args =>
        {
            if (Interlocked.Increment(ref attempts) <= 3) args.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.ServerUnavailable;
            return Task.CompletedTask;
        };
        var delays = new System.Collections.Concurrent.ConcurrentQueue<TimeSpan>();
        await using var resource = broker.Resource(autoConnect: true, reconnectPolicy: new()
        {
            MaxRetryAttempts = 3, Delay = TimeSpan.FromMilliseconds(30), BackoffType = Polly.DelayBackoffType.Exponential,
            MaxDelay = TimeSpan.FromMilliseconds(70), UseJitter = false,
            OnRetry = args => { delays.Enqueue(args.RetryDelay); return ValueTask.CompletedTask; }
        });
        await using var connection = new MqttConnection(new() { Connection = resource });
        var events = Capture(connection.Events);
        await connection.StartAsync();
        await WaitForEvent(events, "mqtt.connected");
        delays.ToArray().ShouldBe(new[] { TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(70) });
        Volatile.Read(ref attempts).ShouldBe(4);
        resource.IsConnected.ShouldBeTrue();
    }

    [Fact]
    public async Task Disconnect_cancels_retry_backoff_and_later_connect_starts_cleanly()
    {
        await using var broker = await Broker.Start();
        var accept = 0;
        var attempts = 0;
        broker.Server.ValidatingConnectionAsync += args =>
        {
            Interlocked.Increment(ref attempts);
            if (Volatile.Read(ref accept) == 0) args.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.ServerUnavailable;
            return Task.CompletedTask;
        };
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var resource = broker.Resource(autoConnect: true, reconnectPolicy: new()
        {
            MaxRetryAttempts = 3, Delay = TimeSpan.FromMinutes(5),
            OnRetry = _ => { retryStarted.TrySetResult(); return ValueTask.CompletedTask; }
        });
        await using var connection = new MqttConnection(new() { Connection = resource });
        var output = Capture(connection.Output);
        await connection.StartAsync();
        await retryStarted.Task.WaitAsync(Deadline);
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        await connection.Input.SendAsync(Message(new { command = "Disconnect", disableReconnect = true }));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        await Task.Delay(100);
        Volatile.Read(ref attempts).ShouldBe(1);
        Volatile.Write(ref accept, 1);
        await connection.Input.SendAsync(Message(new { command = "Connect" }));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        Volatile.Read(ref attempts).ShouldBe(2);
        resource.IsConnected.ShouldBeTrue();
    }

    [Fact]
    public async Task Failed_subscription_restoration_does_not_restart_an_exhausted_retry_budget()
    {
        await using var broker = await Broker.Start();
        var attempts = 0;
        broker.Server.ValidatingConnectionAsync += _ => { Interlocked.Increment(ref attempts); return Task.CompletedTask; };
        broker.Server.InterceptingSubscriptionAsync += args =>
        {
            args.Response.ReasonCode = MQTTnet.Protocol.MqttSubscribeReasonCode.NotAuthorized;
            return Task.CompletedTask;
        };
        await using var resource = broker.Resource(autoConnect: true, reconnectPolicy: new()
        {
            MaxRetryAttempts = 1, Delay = TimeSpan.FromMilliseconds(20)
        });
        await using var connection = new MqttConnection(new() { Connection = resource });
        await using var subscriber = new MqttSubscriber(new() { Connection = resource, Topics = [new() { Topic = "denied" }] });
        var events = Capture(connection.Events);
        await connection.StartAsync();
        await WaitForEvent(events, "mqtt.reconnect_exhausted");
        await Task.Delay(150);
        Volatile.Read(ref attempts).ShouldBe(2);
        resource.IsConnected.ShouldBeFalse();
    }

    [Theory]
    [InlineData("\"MaxRetryAttempts\":-1")]
    [InlineData("\"Delay\":\"-00:00:01\"")]
    [InlineData("\"MaxDelay\":\"-00:00:01\"")]
    [InlineData("\"BackoffType\":123")]
    public void Invalid_retry_configuration_rejects_registration_atomically(string policy)
    {
        var json = $$"""
        { "Resources": { "mqtt": { "Type": "mqtt.client", "Host": "localhost", "ClientId": "test", "Reconnect": { {{policy}} } } },
          "Workflows": { "demo": { "connection": { "Type": "mqtt.connection", "Connection": "mqtt" } } } }
        """;
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var services = new ServiceCollection().AddMqtt();
        var before = services.ToArray();
        Should.Throw<Exception>(() => services.AddFluxFlowApplication(config));
        services.ToArray().ShouldBe(before);
    }

    [Fact]
    public async Task Connection_attempt_timeout_is_retried_without_treating_it_as_application_cancellation()
    {
        await using var broker = await Broker.Start();
        var attempts = 0;
        broker.Server.ValidatingConnectionAsync += async _ =>
        {
            Interlocked.Increment(ref attempts);
            await Task.Delay(500);
        };
        await using var resource = broker.Resource(autoConnect: true, connectTimeout: TimeSpan.FromMilliseconds(100),
            reconnectPolicy: new() { MaxRetryAttempts = 1, Delay = TimeSpan.FromMilliseconds(20) });
        await using var connection = new MqttConnection(new() { Connection = resource });
        var events = Capture(connection.Events);
        var output = Capture(connection.Output);
        await connection.StartAsync();
        await WaitForEvent(events, "mqtt.reconnect_exhausted");
        Volatile.Read(ref attempts).ShouldBe(2);
        (await output.ReceiveAsync(Deadline)).Error!.Message.ShouldContain("timed out");
        (await output.ReceiveAsync(Deadline)).Error!.Message.ShouldContain("timed out");
        connection.StopProduction();
        await connection.Completion.WaitAsync(Deadline);
    }

    private static async Task WaitForEvent(BufferBlock<FlowMessage> events, string name)
    {
        using var timeout = new CancellationTokenSource(Deadline);
        while ((await events.ReceiveAsync(timeout.Token)).Value.Json.GetProperty("Name").GetString() != name) { }
    }

    private sealed class Broker : IAsyncDisposable
    {
        public required MqttServer Server { get; init; }
        public required int Port { get; init; }
        public string ClientId { get; } = Guid.NewGuid().ToString("N");
        public static async Task<Broker> Start(bool persistent = false)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var options = new MqttServerOptionsBuilder()
                .WithDefaultEndpoint().WithDefaultEndpointPort(port).WithDefaultEndpointBoundIPAddress(IPAddress.Loopback).Build();
            options.EnablePersistentSessions = persistent;
            var server = new MqttServerFactory().CreateMqttServer(options);
            await server.StartAsync();
            return new Broker { Server = server, Port = port };
        }
        public MqttClientResource Resource(bool persistent = false, bool autoConnect = false, TimeSpan? reconnectInterval = null,
            RetryStrategyOptions? reconnectPolicy = null, TimeSpan? connectTimeout = null) => new(new()
        {
            Client = new MqttClientOptionsBuilder()
                .WithTcpServer("127.0.0.1", Port).WithClientId(ClientId).WithCleanSession(!persistent)
                .WithSessionExpiryInterval(persistent ? 3600u : 0u).WithTimeout(connectTimeout ?? TimeSpan.FromSeconds(2)).Build(),
            AutoConnect = autoConnect, ReconnectPolicy = reconnectPolicy ?? new()
            {
                MaxRetryAttempts = int.MaxValue, Delay = reconnectInterval ?? TimeSpan.FromSeconds(5)
            }
        });
        public Task Publish(string topic, byte[] payload, bool retain = false) => Server.InjectApplicationMessage(
            new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload)
                .WithRetainFlag(retain).WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce).Build()));
        public async ValueTask DisposeAsync() { await Server.StopAsync(); Server.Dispose(); }
    }
}
