using FluxFlow.Components.Mqtt.RequestReply;
using FluxFlow.Components.RequestReply;
using FluxFlow.Nodes;
using Shouldly;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Mqtt.RequestReply.Tests;

public sealed class MqttRequestReplyTests
{
    [Fact]
    public void CorrelationId_IsSeededFromCorrelationData()
    {
        var context = new MqttRequestContext(
            new MqttRequest { Topic = "rpc/add", CorrelationData = Encoding.UTF8.GetBytes("trace-1") },
            new RecordingPublisher());

        context.CorrelationId.ShouldBe(new CorrelationId("trace-1"));
    }

    [Fact]
    public async Task ReplyAsync_PublishesToResponseTopic_EchoingCorrelationData()
    {
        var publisher = new RecordingPublisher();
        var correlation = Encoding.UTF8.GetBytes("c-1");
        var context = new MqttRequestContext(
            new MqttRequest
            {
                Topic = "rpc/add",
                ResponseTopic = "rpc/add/reply",
                CorrelationData = correlation
            },
            publisher);

        await context.ReplyAsync(new MqttReply { Payload = Encoding.UTF8.GetBytes("42"), ContentType = "text/plain" });

        var published = publisher.Published.ShouldHaveSingleItem();
        published.Topic.ShouldBe("rpc/add/reply");
        Encoding.UTF8.GetString(published.Payload).ShouldBe("42");
        published.CorrelationData.ShouldBe(correlation);    // echoed so the requester can match
        published.ContentType.ShouldBe("text/plain");
    }

    [Fact]
    public async Task ReplyAsync_WithoutResponseTopic_PublishesNothing()
    {
        var publisher = new RecordingPublisher();
        var context = new MqttRequestContext(new MqttRequest { Topic = "fire" }, publisher);

        await context.ReplyAsync(new MqttReply { Payload = Encoding.UTF8.GetBytes("x") });

        publisher.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task EndToEnd_SameBridge_DrivesMqtt()
    {
        // The exact bridge the HTTP trigger uses — here driving MQTT, proving neutrality.
        await using var bridge = new RequestReplyBridge<MqttRequest, MqttReply>();
        var handler = new ActionBlock<FlowMessage<MqttRequest>>(request =>
            bridge.Responses.Post(request.With(new MqttReply
            {
                Payload = Encoding.UTF8.GetBytes($"echo:{Encoding.UTF8.GetString(request.Payload.Payload)}")
            })));
        bridge.Output.LinkTo(handler);

        var publisher = new RecordingPublisher();
        await bridge.SubmitAsync(
            new MqttRequest
            {
                Topic = "rpc/echo",
                ResponseTopic = "rpc/echo/reply",
                CorrelationData = Encoding.UTF8.GetBytes("c-9"),
                Payload = Encoding.UTF8.GetBytes("hi")
            },
            publisher);

        var published = await publisher.WaitForOne().WaitAsync(TimeSpan.FromSeconds(30));
        published.Topic.ShouldBe("rpc/echo/reply");
        Encoding.UTF8.GetString(published.Payload).ShouldBe("echo:hi");
        Encoding.UTF8.GetString(published.CorrelationData!).ShouldBe("c-9");
    }

    private sealed class RecordingPublisher : IMqttResponsePublisher
    {
        private readonly TaskCompletionSource<MqttResponseMessage> _first =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<MqttResponseMessage> Published { get; } = new();

        public Task PublishAsync(MqttResponseMessage message, CancellationToken cancellationToken = default)
        {
            Published.Enqueue(message);
            _first.TrySetResult(message);
            return Task.CompletedTask;
        }

        public Task<MqttResponseMessage> WaitForOne() => _first.Task;
    }
}
