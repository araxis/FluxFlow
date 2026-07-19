using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class FlowValueRoutingNodeTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Window_emits_count_result_with_message_lineage()
    {
        var now = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
        await using var node = new FlowValueWindowNode(
            new WindowRoutingOptions { MaxItems = 2 },
            new FakeTimeProvider(now));
        var results = Link(node.Output);
        var first = FlowMessage.Create(FlowValue.From("first"));
        var second = FlowMessage.Create(FlowValue.From("second"));

        await node.Input.SendAsync(first);
        await node.Input.SendAsync(second);

        var output = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        output.Payload.Kind.ShouldBe(RoutingResultKinds.WindowCount);
        output.Payload.IsError.ShouldBeFalse();
        output.Payload.Value.ShouldNotBeNull().Items.ShouldBe([
            first.Payload,
            second.Payload
        ]);
        output.CorrelationId.ShouldBe(first.CorrelationId);
    }

    [Fact]
    public async Task Correlation_emits_match_and_timeout_on_one_output()
    {
        var now = DateTimeOffset.Parse("2026-07-19T12:05:00Z");
        var clock = new TrackingFakeTimeProvider(now);
        await using var node = new FlowValueCorrelationNode(
            new CorrelationRoutingOptions { TimeoutMilliseconds = 100 },
            Key,
            Side,
            clock: clock);
        var results = Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(Item("one", "request", "left")));
        await node.Input.SendAsync(FlowMessage.Create(Item("one", "response", "right")));

        var matched = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        matched.Payload.Kind.ShouldBe(RoutingResultKinds.Matched);
        var match = matched.Payload.Value
            .ShouldBeOfType<FlowCorrelationMatchedOutcome<FlowValue>>()
            .Match;
        match.Key.ShouldBe("one");
        match.Request.GetObject()["value"].GetString().ShouldBe("left");
        match.Response.GetObject()["value"].GetString().ShouldBe("right");

        var correlationTimer = clock.NextTimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create(Item("two", "request", "pending")));
        await correlationTimer.WaitAsync(WaitTimeout);
        clock.Advance(TimeSpan.FromMilliseconds(100));

        var timedOut = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        timedOut.Payload.Kind.ShouldBe(RoutingResultKinds.TimedOut);
        timedOut.Payload.IsError.ShouldBeFalse();
        timedOut.Payload.Value
            .ShouldBeOfType<FlowCorrelationTimedOutOutcome<FlowValue>>()
            .Timeout.Key.ShouldBe("two");
    }

    [Fact]
    public async Task Correlation_selector_failure_is_normal_error_result()
    {
        await using var node = new FlowValueCorrelationNode(
            new CorrelationRoutingOptions(),
            static _ => throw new InvalidOperationException("selector failed"),
            Side);
        var results = Link(node.Output);
        var input = FlowMessage.Create(Item("one", "request", "value"));

        await node.Input.SendAsync(input);

        var output = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        output.Payload.Kind.ShouldBe(RoutingResultKinds.OperationFailed);
        output.Payload.IsError.ShouldBeTrue();
        output.Payload.Error.ShouldNotBeNull().Code
            .ShouldBe(RoutingErrorCodeNames.OperationFailed);
        output.Payload.Error.Details.GetObject()["legacyCode"].GetInteger()
            .ShouldBe(RoutingErrorCodes.CorrelationKeyFailed);
        output.CorrelationId.ShouldBe(input.CorrelationId);
    }

    [Fact]
    public async Task Join_emits_match_and_timeout_on_one_output()
    {
        var now = DateTimeOffset.Parse("2026-07-19T12:10:00Z");
        var clock = new TrackingFakeTimeProvider(now);
        await using var node = new FlowValueJoinNode(
            new JoinRoutingOptions { TimeoutMilliseconds = 100 },
            Key,
            Key,
            clock: clock);
        var results = Link(node.Output);

        await node.Left.SendAsync(FlowMessage.Create(Item("one", "left", "left")));
        await node.Right.SendAsync(FlowMessage.Create(Item("one", "right", "right")));

        var matched = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        matched.Payload.Kind.ShouldBe(RoutingResultKinds.Matched);
        var match = matched.Payload.Value
            .ShouldBeOfType<FlowJoinMatchedOutcome<FlowValue, FlowValue>>()
            .Match;
        match.Key.ShouldBe("one");
        match.Left.GetObject()["value"].GetString().ShouldBe("left");
        match.Right.GetObject()["value"].GetString().ShouldBe("right");

        var joinTimer = clock.NextTimerScheduled;
        await node.Left.SendAsync(FlowMessage.Create(Item("two", "left", "pending")));
        await joinTimer.WaitAsync(WaitTimeout);
        clock.Advance(TimeSpan.FromMilliseconds(100));

        var timedOut = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        timedOut.Payload.Kind.ShouldBe(RoutingResultKinds.TimedOut);
        timedOut.Payload.Value
            .ShouldBeOfType<FlowJoinTimedOutOutcome<FlowValue, FlowValue>>()
            .Timeout.Key.ShouldBe("two");
    }

    [Fact]
    public async Task Join_selector_failure_is_normal_error_result_with_message_lineage()
    {
        await using var node = new FlowValueJoinNode(
            new JoinRoutingOptions(),
            static _ => throw new InvalidOperationException("selector failed"),
            Key);
        var results = Link(node.Output);
        var input = FlowMessage.Create(Item("one", "left", "value"));

        await node.Left.SendAsync(input);

        var output = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        output.Payload.Kind.ShouldBe(RoutingResultKinds.OperationFailed);
        output.Payload.IsError.ShouldBeTrue();
        output.Payload.Error.ShouldNotBeNull().Code
            .ShouldBe(RoutingErrorCodeNames.OperationFailed);
        output.Payload.Error.Details.GetObject()["legacyCode"].GetInteger()
            .ShouldBe(RoutingErrorCodes.JoinLeftKeyFailed);
        output.CorrelationId.ShouldBe(input.CorrelationId);
    }

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }

    private static string? Key(FlowValue value)
        => value.GetObject()["key"].GetString();

    private static string? Side(FlowValue value)
        => value.GetObject()["side"].GetString();

    private static FlowValue Item(string key, string side, string value)
        => FlowValue.FromObject(new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["key"] = FlowValue.From(key),
            ["side"] = FlowValue.From(side),
            ["value"] = FlowValue.From(value)
        });
}
