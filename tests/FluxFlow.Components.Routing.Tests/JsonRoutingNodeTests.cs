using System.Text.Json;
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

public sealed class JsonRoutingNodeTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Window_emits_count_result_with_message_lineage()
    {
        var now = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
        await using var node = new JsonWindowNode(
            new WindowRoutingOptions { MaxItems = 2 },
            new FakeTimeProvider(now));
        var results = Link(node.Output);
        var first = FlowMessage.Create(JsonSerializer.SerializeToElement("first"));
        var second = FlowMessage.Create(JsonSerializer.SerializeToElement("second"));

        await node.Input.SendAsync(first);
        await node.Input.SendAsync(second);

        var output = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        output.IsError.ShouldBeFalse();
        output.Value.Items.ShouldBe([
            first.Value,
            second.Value
        ]);
        output.CorrelationId.ShouldBe(first.CorrelationId);
    }

    [Fact]
    public async Task Correlation_emits_match_and_timeout_on_one_output()
    {
        var now = DateTimeOffset.Parse("2026-07-19T12:05:00Z");
        var clock = new TrackingFakeTimeProvider(now);
        await using var node = new JsonCorrelationNode(
            new CorrelationRoutingOptions { TimeoutMilliseconds = 100 },
            Key,
            Side,
            clock: clock);
        var results = Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(Item("one", "request", "left")));
        await node.Input.SendAsync(FlowMessage.Create(Item("one", "response", "right")));

        var matched = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var match = matched.Value
            .ShouldBeOfType<FlowCorrelationMatchedOutcome<JsonElement>>()
            .Match;
        match.Key.ShouldBe("one");
        match.Request.GetProperty("value").GetString().ShouldBe("left");
        match.Response.GetProperty("value").GetString().ShouldBe("right");

        var correlationTimer = clock.NextTimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create(Item("two", "request", "pending")));
        await correlationTimer.WaitAsync(WaitTimeout);
        clock.Advance(TimeSpan.FromMilliseconds(100));

        var timedOut = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        timedOut.IsError.ShouldBeFalse();
        timedOut.Value
            .ShouldBeOfType<FlowCorrelationTimedOutOutcome<JsonElement>>()
            .Timeout.Key.ShouldBe("two");
    }

    [Fact]
    public async Task Correlation_selector_failure_is_normal_error_result()
    {
        await using var node = new JsonCorrelationNode(
            new CorrelationRoutingOptions(),
            static _ => throw new InvalidOperationException("selector failed"),
            Side);
        var results = Link(node.Output);
        var input = FlowMessage.Create(Item("one", "request", "value"));

        await node.Input.SendAsync(input);

        var output = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        output.IsError.ShouldBeTrue();
        output.Error.ShouldNotBeNull().Code
            .ShouldBe(RoutingErrorCodeNames.OperationFailed);
        output.Error.Details!.Value.GetProperty("legacyCode").GetInt32()
            .ShouldBe(RoutingErrorCodes.CorrelationKeyFailed);
        output.CorrelationId.ShouldBe(input.CorrelationId);
    }

    [Fact]
    public async Task Join_emits_match_and_timeout_on_one_output()
    {
        var now = DateTimeOffset.Parse("2026-07-19T12:10:00Z");
        var clock = new TrackingFakeTimeProvider(now);
        await using var node = new JsonJoinNode(
            new JoinRoutingOptions { TimeoutMilliseconds = 100 },
            Key,
            Key,
            clock: clock);
        var results = Link(node.Output);

        await node.Left.SendAsync(FlowMessage.Create(Item("one", "left", "left")));
        await node.Right.SendAsync(FlowMessage.Create(Item("one", "right", "right")));

        var matched = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var match = matched.Value
            .ShouldBeOfType<FlowJoinMatchedOutcome<JsonElement, JsonElement>>()
            .Match;
        match.Key.ShouldBe("one");
        match.Left.GetProperty("value").GetString().ShouldBe("left");
        match.Right.GetProperty("value").GetString().ShouldBe("right");

        var joinTimer = clock.NextTimerScheduled;
        await node.Left.SendAsync(FlowMessage.Create(Item("two", "left", "pending")));
        await joinTimer.WaitAsync(WaitTimeout);
        clock.Advance(TimeSpan.FromMilliseconds(100));

        var timedOut = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        timedOut.Value
            .ShouldBeOfType<FlowJoinTimedOutOutcome<JsonElement, JsonElement>>()
            .Timeout.Key.ShouldBe("two");
    }

    [Fact]
    public async Task Join_selector_failure_is_normal_error_result_with_message_lineage()
    {
        await using var node = new JsonJoinNode(
            new JoinRoutingOptions(),
            static _ => throw new InvalidOperationException("selector failed"),
            Key);
        var results = Link(node.Output);
        var input = FlowMessage.Create(Item("one", "left", "value"));

        await node.Left.SendAsync(input);

        var output = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        output.IsError.ShouldBeTrue();
        output.Error.ShouldNotBeNull().Code
            .ShouldBe(RoutingErrorCodeNames.OperationFailed);
        output.Error.Details!.Value.GetProperty("legacyCode").GetInt32()
            .ShouldBe(RoutingErrorCodes.JoinLeftKeyFailed);
        output.CorrelationId.ShouldBe(input.CorrelationId);
    }

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }

    private static string? Key(JsonElement value)
        => value.GetProperty("key").GetString();

    private static string? Side(JsonElement value)
        => value.GetProperty("side").GetString();

    private static JsonElement Item(string key, string side, string value)
        => JsonSerializer.SerializeToElement(new { key, side, value });
}
