using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Nodes;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttTriggerNodeTests
{
    [Fact]
    public async Task TriggerNode_ReportsNotConnectedWhenTriggerSourceUnavailable()
    {
        var triggerSource = new UnavailableTriggerSource();
        await using var node = new MqttTriggerNode(
            triggerSource,
            new MqttTriggerOptions
            {
                TopicFilter = "devices/+",
                QualityOfService = MqttQualityOfService.AtLeastOnce,
                ReceiveRetainedMessages = false,
                RetainAsPublished = true,
                BoundedCapacity = 4
            });

        var messages = MqttTestContext.Sink(node.Output, propagateCompletion: true);
        var errors = MqttTestContext.Sink(node.Errors);

        await node.StartAsync();

        var flowError = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        flowError.Code.ShouldBe(MqttErrorCodes.TriggerNotConnected);
        flowError.Context.ShouldNotBeNull();
        flowError.Context.ShouldContain("topicFilter=devices/+");
        flowError.Message.ShouldContain("not started");

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        messages.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task TriggerNode_SubscribesAndFlowsMessages()
    {
        var triggerSource = new RecordingMqttClientAdapter();
        await using var node = new MqttTriggerNode(
            triggerSource,
            new MqttTriggerOptions { TopicFilter = "devices/+" });

        var messages = MqttTestContext.Sink(node.Output);

        await node.StartAsync();
        await triggerSource.Subscribed.WaitAsync(TimeSpan.FromSeconds(5));
        triggerSource.SubscribeCalls.ShouldBe(1);

        triggerSource.PushMessage(CreateMessage("devices/a", "m1"));
        var received = await messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        received.Payload.Topic.ShouldBe("devices/a");
        received.Payload.CorrelationId.ShouldBe("m1");
        received.CorrelationId.ShouldBe(new CorrelationId("m1"));
    }

    [Fact]
    public async Task TriggerNode_AcknowledgesOnEmitWhenConfigured()
    {
        var triggerSource = new RecordingMqttClientAdapter();
        await using var node = new MqttTriggerNode(
            triggerSource,
            new MqttTriggerOptions
            {
                TopicFilter = "devices/+",
                Acknowledgement = MqttTriggerAcknowledgement.OnEmit
            });

        var messages = MqttTestContext.Sink(node.Output);

        await node.StartAsync();
        await triggerSource.Subscribed.WaitAsync(TimeSpan.FromSeconds(5));

        var context = triggerSource.PushMessage(CreateMessage("devices/a", "m1"));
        await messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await context.Acked.WaitAsync(TimeSpan.FromSeconds(5));

        context.AckCalls.ShouldBe(1);
        context.NackCalls.ShouldBe(0);
    }

    [Fact]
    public async Task TriggerNode_RequestReplyAcknowledgesOnSuccessResponse()
    {
        var triggerSource = new RecordingMqttClientAdapter();
        await using var node = new MqttTriggerNode(
            triggerSource,
            new MqttTriggerOptions
            {
                TopicFilter = "devices/+",
                Mode = MqttTriggerMode.RequestReply,
                Acknowledgement = MqttTriggerAcknowledgement.OnSuccessfulResponse
            });

        var messages = MqttTestContext.Sink(node.Output);

        await node.StartAsync();
        await triggerSource.Subscribed.WaitAsync(TimeSpan.FromSeconds(5));

        var context = triggerSource.PushMessage(CreateMessage("devices/a", "m1"));
        var received = await messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        (await node.Responses.SendAsync(received.With(MqttTriggerResponse.Success()))
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        await context.Acked.WaitAsync(TimeSpan.FromSeconds(5));

        context.AckCalls.ShouldBe(1);
        context.NackCalls.ShouldBe(0);
    }

    [Fact]
    public async Task TriggerNode_RequestReplyRejectsOnFailureResponse()
    {
        var triggerSource = new RecordingMqttClientAdapter();
        await using var node = new MqttTriggerNode(
            triggerSource,
            new MqttTriggerOptions
            {
                TopicFilter = "devices/+",
                Mode = MqttTriggerMode.RequestReply,
                Acknowledgement = MqttTriggerAcknowledgement.OnSuccessfulResponse
            });

        var messages = MqttTestContext.Sink(node.Output);

        await node.StartAsync();
        await triggerSource.Subscribed.WaitAsync(TimeSpan.FromSeconds(5));

        var context = triggerSource.PushMessage(CreateMessage("devices/a", "m1"));
        var received = await messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        (await node.Responses.SendAsync(received.With(MqttTriggerResponse.Failure("handler failed")))
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        var error = await context.Nacked.WaitAsync(TimeSpan.FromSeconds(5));

        context.AckCalls.ShouldBe(0);
        context.NackCalls.ShouldBe(1);
        error.ShouldNotBeNull();
        error.Message.ShouldContain("handler failed");
    }

    [Fact]
    public async Task TriggerNode_RequestReplyRejectsOnTimeout()
    {
        var triggerSource = new RecordingMqttClientAdapter();
        var clock = new TrackingFakeTimeProvider(
            DateTimeOffset.Parse("2026-06-20T00:00:00+00:00"));
        await using var node = new MqttTriggerNode(
            triggerSource,
            new MqttTriggerOptions
            {
                TopicFilter = "devices/+",
                Mode = MqttTriggerMode.RequestReply,
                Acknowledgement = MqttTriggerAcknowledgement.OnSuccessfulResponse,
                ResponseTimeout = TimeSpan.FromSeconds(5)
            },
            clock);

        var messages = MqttTestContext.Sink(node.Output);

        await node.StartAsync();
        await triggerSource.Subscribed.WaitAsync(TimeSpan.FromSeconds(5));

        var context = triggerSource.PushMessage(CreateMessage("devices/a", "m1"));
        await messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        clock.Advance(TimeSpan.FromSeconds(5));
        var error = await context.Nacked.WaitAsync(TimeSpan.FromSeconds(5));

        context.AckCalls.ShouldBe(0);
        context.NackCalls.ShouldBe(1);
        error.ShouldBeOfType<TimeoutException>();
    }

    [Fact]
    public async Task TriggerNode_RequestReplyAckOnEmitDoesNotRejectAgainOnStop()
    {
        var triggerSource = new RecordingMqttClientAdapter();
        await using var node = new MqttTriggerNode(
            triggerSource,
            new MqttTriggerOptions
            {
                TopicFilter = "devices/+",
                Mode = MqttTriggerMode.RequestReply,
                Acknowledgement = MqttTriggerAcknowledgement.OnEmit
            });

        var messages = MqttTestContext.Sink(node.Output);

        await node.StartAsync();
        await triggerSource.Subscribed.WaitAsync(TimeSpan.FromSeconds(5));

        var context = triggerSource.PushMessage(CreateMessage("devices/a", "m1"));
        await messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await context.Acked.WaitAsync(TimeSpan.FromSeconds(5));

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        context.AckCalls.ShouldBe(1);
        context.NackCalls.ShouldBe(0);
    }

    [Fact]
    public async Task TriggerNode_DisposesSubscriptionOnComplete()
    {
        var triggerSource = new RecordingMqttClientAdapter();
        await using var node = new MqttTriggerNode(
            triggerSource,
            new MqttTriggerOptions { TopicFilter = "devices/+" });

        await node.StartAsync();
        await triggerSource.Subscribed.WaitAsync(TimeSpan.FromSeconds(5));
        var subscription = triggerSource.Subscriptions[0];

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        subscription.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task TriggerNode_CompletesCleanlyOnComplete()
    {
        var triggerSource = new RecordingMqttClientAdapter();
        await using var node = new MqttTriggerNode(
            triggerSource,
            new MqttTriggerOptions { TopicFilter = "devices/+" });

        await node.StartAsync();
        node.Completion.IsCompleted.ShouldBeFalse();

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task TriggerNode_DisposeCompletesNode()
    {
        var triggerSource = new RecordingMqttClientAdapter();
        var node = new MqttTriggerNode(
            triggerSource,
            new MqttTriggerOptions { TopicFilter = "devices/#" });

        await node.StartAsync();
        await node.DisposeAsync();

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public void TriggerNode_RejectsNullTriggerSource()
        => Should.Throw<ArgumentNullException>(() => new MqttTriggerNode(triggerSource: null!));

    [Fact]
    public void TriggerNode_RejectsSuccessfulResponseAckWithoutRequestReplyMode()
        => Should.Throw<ArgumentException>(() => new MqttTriggerNode(
            new RecordingMqttClientAdapter(),
            new MqttTriggerOptions
            {
                TopicFilter = "devices/+",
                Acknowledgement = MqttTriggerAcknowledgement.OnSuccessfulResponse
            }));

    [Fact]
    public void TriggerNode_RejectsInvalidQualityOfServiceOption()
        => Should.Throw<ArgumentOutOfRangeException>(() => new MqttTriggerNode(
            new RecordingMqttClientAdapter(),
            new MqttTriggerOptions
            {
                TopicFilter = "devices/+",
                QualityOfService = (MqttQualityOfService)99
            }));

    [Fact]
    public void TriggerNode_RejectsInvalidMode()
        => Should.Throw<ArgumentOutOfRangeException>(() => new MqttTriggerNode(
            new RecordingMqttClientAdapter(),
            new MqttTriggerOptions
            {
                TopicFilter = "devices/+",
                Mode = (MqttTriggerMode)99
            }));

    [Fact]
    public void TriggerNode_RejectsInvalidAcknowledgement()
        => Should.Throw<ArgumentOutOfRangeException>(() => new MqttTriggerNode(
            new RecordingMqttClientAdapter(),
            new MqttTriggerOptions
            {
                TopicFilter = "devices/+",
                Acknowledgement = (MqttTriggerAcknowledgement)99
            }));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("devices/#/state")]
    [InlineData("devices/temperature#")]
    [InlineData("devices/+/state+")]
    public void TriggerNode_RejectsInvalidTopicFilter(string? topicFilter)
        => Should.Throw<ArgumentException>(() => new MqttTriggerNode(
            new RecordingMqttClientAdapter(),
            new MqttTriggerOptions { TopicFilter = topicFilter }));

    private sealed class UnavailableTriggerSource : IMqttTriggerSource
    {
        public ValueTask<IMqttSubscription> SubscribeAsync(
            MqttTriggerOptions options,
            CancellationToken cancellationToken = default)
            => throw new MqttClientUnavailableException("MQTT trigger source is not started.");
    }

    private static MqttReceivedMessage CreateMessage(string topic, string correlationId)
        => new()
        {
            Timestamp = DateTimeOffset.UtcNow,
            Topic = topic,
            Payload = [1, 2, 3],
            CorrelationId = correlationId
        };
}
