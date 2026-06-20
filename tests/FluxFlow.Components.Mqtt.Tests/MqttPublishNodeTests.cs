using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Components.Mqtt.Nodes;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttPublishNodeTests
{
    [Fact]
    public async Task PublishNode_ReportsNotConnectedForUnavailablePublisher()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 2, 3, 7, 1, 2, TimeSpan.Zero));
        var publisher = new UnavailablePublisher();
        await using var node = new MqttPublishNode(
            publisher,
            new MqttPublishOptions
            {
                BoundedCapacity = 4
            },
            clock);

        var results = MqttTestContext.Sink(node.Output);
        var errors = MqttTestContext.Sink(node.Errors);
        var events = MqttTestContext.Sink(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(
            new MqttPublishRequest
            {
                Topic = "devices/temperature",
                Payload = [1, 2, 3],
                PayloadPreview = "010203",
                Properties = new MqttPublishProperties { CorrelationId = "abc" }
            },
            new CorrelationId("corr-1")));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MqttErrorCodes.PublishNotConnected);
        error.Message.ShouldContain("not started");
        error.CorrelationId.ShouldBe(new CorrelationId("corr-1"));
        error.Context.ShouldNotBeNull();
        error.Context.ShouldContain("mqttCorrelationId=abc");

        var flowEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        flowEvent.Name.ShouldBe(MqttEventNames.PublishFailed);
        flowEvent.Level.ShouldBe(FlowEventLevel.Error);
        flowEvent.CorrelationId.ShouldBe(new CorrelationId("corr-1"));
        flowEvent.Timestamp.ShouldBe(clock.GetUtcNow());
        flowEvent.Attributes["topic"].ShouldBe("devices/temperature");
        flowEvent.Attributes["mqttCorrelationId"].ShouldBe("abc");

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        results.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task PublishNode_RoundTripsThroughPublisher()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 2, 3, 7, 1, 2, TimeSpan.Zero));
        var publisher = new RecordingMqttClientAdapter();
        await using var node = new MqttPublishNode(
            publisher,
            new MqttPublishOptions
            {
                BoundedCapacity = 8
            },
            clock);

        var results = MqttTestContext.Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(
            new MqttPublishRequest
            {
                Topic = "devices/temperature",
                Payload = [1, 2, 3],
                QualityOfService = MqttQualityOfService.AtLeastOnce,
                Retain = true,
                Properties = new MqttPublishProperties { CorrelationId = "after" }
            },
            new CorrelationId("envelope-after")));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.CorrelationId.ShouldBe(new CorrelationId("envelope-after"));
        result.Payload.Topic.ShouldBe("devices/temperature");
        result.Payload.PayloadBytes.ShouldBe(3);
        result.Payload.QualityOfService.ShouldBe(MqttQualityOfService.AtLeastOnce);
        result.Payload.Retain.ShouldBeTrue();
        result.Payload.Properties.ShouldNotBeNull();
        result.Payload.Properties.CorrelationId.ShouldBe("after");
        publisher.Published
            .Any(request =>
                request.Properties?.CorrelationId == "after" &&
                request.QualityOfService == MqttQualityOfService.AtLeastOnce &&
                request.Retain)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task PublishNode_ReportsErrorWhenTopicIsMissing()
    {
        var publisher = new RecordingMqttClientAdapter();
        await using var node = new MqttPublishNode(publisher);
        var errors = MqttTestContext.Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new MqttPublishRequest
        {
            Topic = null!,
            Payload = [1]
        }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MqttErrorCodes.PublishInvalidTopic);
        error.Message.ShouldContain("topic");
    }

    [Fact]
    public async Task PublishNode_ReportsErrorWhenTopicContainsWildcard()
    {
        var publisher = new RecordingMqttClientAdapter();
        await using var node = new MqttPublishNode(publisher);
        var errors = MqttTestContext.Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(
            new MqttPublishRequest { Topic = "devices/+", Payload = [1] }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MqttErrorCodes.PublishInvalidTopic);
        error.Message.ShouldContain("wildcard");
    }

    [Fact]
    public async Task PublishNode_ReportsErrorWhenPayloadIsMissing()
    {
        var publisher = new RecordingMqttClientAdapter();
        await using var node = new MqttPublishNode(publisher);
        var errors = MqttTestContext.Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new MqttPublishRequest
        {
            Topic = "devices/state",
            Payload = null!
        }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MqttErrorCodes.PublishInvalidPayload);
        error.Message.ShouldContain("payload");
    }

    [Fact]
    public void PublishNode_RejectsNullPublisher()
        => Should.Throw<ArgumentNullException>(() => new MqttPublishNode(publisher: null!));

    [Fact]
    public void PublishNode_RejectsInvalidBoundedCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(() => new MqttPublishNode(
            new RecordingMqttClientAdapter(),
            new MqttPublishOptions { BoundedCapacity = 0 }));

    [Fact]
    public void PublishNode_RejectsInvalidPublishTimeout()
        => Should.Throw<ArgumentOutOfRangeException>(() => new MqttPublishNode(
            new RecordingMqttClientAdapter(),
            new MqttPublishOptions { PublishTimeoutMilliseconds = 0 }));

    [Fact]
    public async Task PublishNode_ReportsErrorWhenRequestQualityOfServiceIsInvalid()
    {
        var publisher = new RecordingMqttClientAdapter();
        await using var node = new MqttPublishNode(publisher);
        var errors = MqttTestContext.Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new MqttPublishRequest
        {
            Topic = "devices/state",
            Payload = [1],
            QualityOfService = (MqttQualityOfService)99
        }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MqttErrorCodes.PublishInvalidQualityOfService);
        error.Message.ShouldContain("quality");
    }

    private sealed class UnavailablePublisher : IMqttPublisher
    {
        public ValueTask PublishAsync(
            MqttPublishRequest request,
            CancellationToken cancellationToken = default)
            => throw new MqttClientUnavailableException("MQTT publisher is not started.");
    }
}
