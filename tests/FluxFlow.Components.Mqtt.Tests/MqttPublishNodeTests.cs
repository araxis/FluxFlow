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
    public async Task PublishNode_ReportsNotConnectedForValidRequest()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 2, 3, 7, 1, 2, TimeSpan.Zero));
        await using var connection = MqttTestContext.CreateConnection(new ThrowingMqttClientFactory(), clock);
        await using var node = new MqttPublishNode(
            connection,
            new MqttPublishOptions
            {
                DefaultTopic = "devices/temperature",
                Retain = true,
                QualityOfService = MqttQualityOfService.AtLeastOnce,
                BoundedCapacity = 4
            },
            clock);

        var results = MqttTestContext.Sink(node.Output);
        var errors = MqttTestContext.Sink(node.Errors);
        var events = MqttTestContext.Sink(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(
            new MqttPublishRequest
            {
                Payload = [1, 2, 3],
                PayloadPreview = "010203",
                CorrelationId = "abc"
            },
            new CorrelationId("corr-1")));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MqttErrorCodes.PublishNotConnected);
        error.Message.ShouldContain("not connected");
        error.CorrelationId.ShouldBe(new CorrelationId("corr-1"));
        error.Context.ShouldNotBeNull();
        error.Context.ShouldContain("correlationId=abc");
        error.Context.ShouldContain($"connectionName={MqttTestContext.ConnectionName}");

        var flowEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        flowEvent.Name.ShouldBe(MqttEventNames.PublishFailed);
        flowEvent.Level.ShouldBe(FlowEventLevel.Error);
        flowEvent.CorrelationId.ShouldBe(new CorrelationId("corr-1"));
        flowEvent.Timestamp.ShouldBe(clock.GetUtcNow());
        flowEvent.Attributes["topic"].ShouldBe("devices/temperature");
        flowEvent.Attributes["correlationId"].ShouldBe("abc");

        // No client => no result is produced.
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        results.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task PublishNode_RoundTripsThroughBorrowedAdapterAcrossConnectLifecycle()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 2, 3, 7, 1, 2, TimeSpan.Zero));
        var adapter = new RecordingMqttClientAdapter();
        var factory = new RecordingMqttClientFactory(adapter, ownLease: false);
        await using var connection = MqttTestContext.CreateConnection(factory, clock);
        await using var node = new MqttPublishNode(
            connection,
            new MqttPublishOptions
            {
                DefaultTopic = "devices/temperature",
                QualityOfService = MqttQualityOfService.AtLeastOnce,
                BoundedCapacity = 8
            },
            clock);

        var results = MqttTestContext.Sink(node.Output);
        var errors = MqttTestContext.Sink(node.Errors);

        // Before connect: not connected.
        await node.Input.SendAsync(FlowMessage.Create(
            new MqttPublishRequest { Payload = [1], CorrelationId = "before" }));
        var beforeError = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        beforeError.Code.ShouldBe(MqttErrorCodes.PublishNotConnected);

        // After host ConnectAsync: round-trips through the borrowed adapter, carrying the
        // envelope correlation id forward onto the result.
        await connection.ConnectAsync();
        await node.Input.SendAsync(FlowMessage.Create(
            new MqttPublishRequest { Payload = [1, 2, 3], CorrelationId = "after" },
            new CorrelationId("envelope-after")));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.CorrelationId.ShouldBe(new CorrelationId("envelope-after"));
        result.Payload.Topic.ShouldBe("devices/temperature");
        result.Payload.PayloadBytes.ShouldBe(3);
        result.Payload.CorrelationId.ShouldBe("after");
        adapter.Published.ShouldContain(request => request.CorrelationId == "after");

        // After DisconnectAsync: not connected again.
        await connection.DisconnectAsync();
        await node.Input.SendAsync(FlowMessage.Create(
            new MqttPublishRequest { Payload = [9], CorrelationId = "afterDisconnect" }));
        var afterError = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        afterError.Code.ShouldBe(MqttErrorCodes.PublishNotConnected);
    }

    [Fact]
    public async Task PublishNode_ReportsErrorWhenTopicIsMissing()
    {
        await using var connection = MqttTestContext.CreateConnection(new ThrowingMqttClientFactory());
        await using var node = new MqttPublishNode(connection);
        var errors = MqttTestContext.Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new MqttPublishRequest { Payload = [1] }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MqttErrorCodes.PublishInvalidTopic);
        error.Message.ShouldContain("topic");
    }

    [Fact]
    public async Task PublishNode_ReportsErrorWhenTopicContainsWildcard()
    {
        await using var connection = MqttTestContext.CreateConnection(new ThrowingMqttClientFactory());
        await using var node = new MqttPublishNode(connection);
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
        await using var connection = MqttTestContext.CreateConnection(new ThrowingMqttClientFactory());
        await using var node = new MqttPublishNode(
            connection,
            new MqttPublishOptions { DefaultTopic = "devices/state" });
        var errors = MqttTestContext.Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new MqttPublishRequest { Payload = null! }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MqttErrorCodes.PublishInvalidPayload);
        error.Message.ShouldContain("payload");
    }

    [Fact]
    public void PublishNode_RejectsNullConnection()
        => Should.Throw<ArgumentNullException>(() => new MqttPublishNode(connection: null!));
}
