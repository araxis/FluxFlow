using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class CorrelationNodeTests
{
    private sealed record CorrelationMessage(string Key, string Side, string Payload);

    [Fact]
    public async Task Correlation_matches_request_and_response_with_request_lineage()
    {
        var timestamp = DateTimeOffset.Parse("2026-01-01T00:00:04Z");
        await using var node = CreateNode(clock: new FakeTimeProvider(timestamp));
        var output = RoutingTestSink.Link(node.Output);
        var request = Message(new CorrelationMessage("A-100", "request", "start"));

        await node.Input.SendAsync(request);
        await node.Input.SendAsync(Message(
            new CorrelationMessage("A-100", "response", "done")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var message = (await RoutingTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var match = message.Value.ShouldNotBeNull().Deserialize<FlowCorrelationMatchedOutcome<CorrelationMessage>>().ShouldNotBeNull().Match;
        match.Key.ShouldBe("A-100");
        match.Request.Payload.ShouldBe("start");
        match.Response.Payload.ShouldBe("done");
        match.RequestReceivedAt.ShouldBe(timestamp);
        match.ResponseReceivedAt.ShouldBe(timestamp);
        match.MatchedAt.ShouldBe(timestamp);
        message.CorrelationId.ShouldBe(request.CorrelationId);
    }

    [Fact]
    public async Task Correlation_matches_out_of_order_inputs()
    {
        await using var node = CreateNode();
        var output = RoutingTestSink.Link(node.Output);

        await node.Input.SendAsync(Message(
            new CorrelationMessage("A-100", "response", "done")));
        await node.Input.SendAsync(Message(
            new CorrelationMessage("A-100", "request", "start")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var match = (await RoutingTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem()
            .Value.ShouldNotBeNull().Deserialize<FlowCorrelationMatchedOutcome<CorrelationMessage>>().ShouldNotBeNull().Match;
        match.Request.Payload.ShouldBe("start");
        match.Response.Payload.ShouldBe("done");
    }

    [Fact]
    public async Task Correlation_emits_pending_timeout_on_completion()
    {
        await using var node = CreateNode(options => options with { TimeoutMilliseconds = 10 });
        var output = RoutingTestSink.Link(node.Output);
        var request = Message(new CorrelationMessage("A-100", "request", "start"));

        await node.Input.SendAsync(request);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var message = (await RoutingTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var timeout = message.Value.ShouldNotBeNull().Deserialize<FlowCorrelationTimedOutOutcome<CorrelationMessage>>().ShouldNotBeNull().Timeout;
        timeout.Key.ShouldBe("A-100");
        timeout.Side.ShouldBe("request");
        timeout.Value.Payload.ShouldBe("start");
        timeout.Timeout.ShouldBe(TimeSpan.FromMilliseconds(10));
        message.CorrelationId.ShouldBe(request.CorrelationId);
    }

    [Fact]
    public async Task Correlation_uses_configured_clock_for_timeout()
    {
        var startedAt = DateTimeOffset.Parse("2026-01-01T00:00:05Z");
        var clock = new TrackingFakeTimeProvider(startedAt);
        await using var node = CreateNode(
            options => options with { TimeoutMilliseconds = 25 },
            clock);
        var output = RoutingTestSink.Link(node.Output);
        var timerScheduled = clock.NextTimerScheduled;

        await node.Input.SendAsync(Message(
            new CorrelationMessage("A-100", "request", "start")));
        await timerScheduled.WaitAsync(TimeSpan.FromSeconds(30));
        output.TryReceive(out _).ShouldBeFalse();

        clock.Advance(TimeSpan.FromMilliseconds(25));
        var message = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var timeout = message.Value.ShouldNotBeNull().Deserialize<FlowCorrelationTimedOutOutcome<CorrelationMessage>>().ShouldNotBeNull().Timeout;
        timeout.ReceivedAt.ShouldBe(startedAt);
        timeout.TimedOutAt.ShouldBe(startedAt.AddMilliseconds(25));

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Correlation_reports_selector_failure_and_continues()
    {
        await using var node = new CorrelationNode<CorrelationMessage>(
            new CorrelationRoutingOptions { ExpressionName = "pairing" },
            input => input.Payload == "throw"
                ? throw new InvalidOperationException("key failed")
                : input.Key,
            input => input.Side);
        var output = RoutingTestSink.Link(node.Output);

        await node.Input.SendAsync(Message(
            new CorrelationMessage("A-100", "request", "throw")));
        await node.Input.SendAsync(Message(
            new CorrelationMessage("A-101", "request", "start")));
        await node.Input.SendAsync(Message(
            new CorrelationMessage("A-101", "response", "done")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var messages = await RoutingTestSink.DrainUntilCompletedAsync(output);
        var error = messages.Single(message => message.IsError).Error.ShouldNotBeNull();
        error.Code.ShouldBe(RoutingErrorCodeNames.OperationFailed);
        error.Details!.Value.GetProperty("legacyCode").GetInt32()
            .ShouldBe(RoutingErrorCodes.CorrelationKeyFailed);
        error.Details.Value.GetProperty("context").GetString()!.ShouldContain("expressionName=pairing");
        messages.Single(message => !message.IsError).Value
            .ShouldNotBeNull().Deserialize<FlowCorrelationMatchedOutcome<CorrelationMessage>>().ShouldNotBeNull()
            .Match.Key.ShouldBe("A-101");
    }

    [Fact]
    public async Task Correlation_reports_invalid_side_and_capacity_as_data()
    {
        await using var node = CreateNode(options => options with { MaxPending = 1 });
        var output = RoutingTestSink.Link(node.Output);

        await node.Input.SendAsync(Message(
            new CorrelationMessage("invalid", "other", "bad")));
        await node.Input.SendAsync(Message(
            new CorrelationMessage("A-100", "request", "start")));
        await node.Input.SendAsync(Message(
            new CorrelationMessage("A-101", "request", "next")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var errors = (await RoutingTestSink.DrainUntilCompletedAsync(output))
            .Where(message => message.IsError)
            .Select(message => message.Error!.Details!.Value.GetProperty("legacyCode").GetInt32())
            .ToArray();
        errors.ShouldContain(RoutingErrorCodes.CorrelationInvalidSide);
        errors.ShouldContain(RoutingErrorCodes.CorrelationCapacityExceeded);
    }

    [Fact]
    public async Task Correlation_emits_match_diagnostic()
    {
        await using var node = CreateNode(options => options with { ExpressionId = "corr-v1" });
        var events = RoutingTestSink.Link(node.Events);
        node.Output.LinkTo(System.Threading.Tasks.Dataflow.DataflowBlock.NullTarget<
            FlowMessage>());

        await node.Input.SendAsync(Message(
            new CorrelationMessage("A-100", "request", "start")));
        await node.Input.SendAsync(Message(
            new CorrelationMessage("A-100", "response", "done")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var matched = (await RoutingTestSink.DrainUntilCompletedAsync(events))
            .Single(@event => @event.ToFlowEvent().Name == RoutingDiagnosticNames.CorrelationMatched);
        matched.ToFlowEvent().Attributes["key"].ShouldBe("A-100");
        matched.ToFlowEvent().Attributes["expressionId"].ShouldBe("corr-v1");
    }

    [Fact]
    public async Task Correlation_propagates_incoming_error()
    {
        await using var node = CreateNode();
        var output = RoutingTestSink.Link(node.Output);
        var input = FlowMessage.CreateError(
            new FlowError("input.failed", "Input failed.", "test"));

        await node.Input.SendAsync(input);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var propagated = (await RoutingTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        propagated.IsError.ShouldBeTrue();
        propagated.Error!.Code.ShouldBe("input.failed");
        propagated.TraceId.ShouldBe(input.TraceId);
        propagated.ShouldBeSameAs(input);
    }

    [Fact]
    public async Task Correlation_dispose_after_fault_does_not_throw()
    {
        var node = CreateNode();
        node.Fault(new InvalidOperationException("boom"));
        await node.DisposeAsync();
        node.Completion.IsFaulted.ShouldBeTrue();
    }

    [Fact]
    public void Correlation_rejects_invalid_configuration()
    {
        Should.Throw<ArgumentException>(
            () => CreateNode(options => options with { RequestSide = "message", ResponseSide = "message" }));
        Should.Throw<ArgumentOutOfRangeException>(
            () => CreateNode(options => options with { BoundedCapacity = 0 }));
        Should.Throw<ArgumentNullException>(
            () => new CorrelationNode<CorrelationMessage>(
                null!, input => input.Key, input => input.Side));
        Should.Throw<ArgumentNullException>(
            () => new CorrelationNode<CorrelationMessage>(
                new CorrelationRoutingOptions(), null!, input => input.Side));
    }

    [Fact]
    public async Task Correlation_runs_as_a_direct_component_with_canonical_request_reply()
    {
        await using var application = new FlowApplication();
        var instance = CreateNode();
        var component = application.AddWorkflow("main").Add("correlation", instance);
        component.Instance.ShouldBeSameAs(instance);
        application.RepliesFrom(component.Output);
        await application.StartAsync();

        var pending = application.RequestAsync(component.Input, component.Output,
            Message(new CorrelationMessage("key", "request", "left")), TimeSpan.FromSeconds(5));
        await application.SendAsync(component.Input,
            Message(new CorrelationMessage("key", "response", "right")));
        var reply = await pending;

        reply.IsError.ShouldBeFalse();
        var match = reply.Value.ShouldNotBeNull()
            .Deserialize<FlowCorrelationMatchedOutcome<CorrelationMessage>>().ShouldNotBeNull().Match;
        match.Request.Payload.ShouldBe("left");
        match.Response.Payload.ShouldBe("right");
        await application.StopAsync();
    }

    [Fact]
    public async Task Correlation_reports_invalid_materialization_and_then_matches_valid_inputs()
    {
        await using var node = CreateNode();
        var output = RoutingTestSink.Link(node.Output);
        (await node.Input.SendAsync(Message("not an object"))).ShouldBeTrue();
        (await node.Input.SendAsync(Message(new CorrelationMessage("key", "request", "left")))).ShouldBeTrue();
        (await node.Input.SendAsync(Message(new CorrelationMessage("key", "response", "right")))).ShouldBeTrue();
        node.Complete();

        var results = await RoutingTestSink.DrainUntilCompletedAsync(output);

        results.Count.ShouldBe(2);
        results[0].Error.ShouldNotBeNull().Code.ShouldBe(RoutingErrorCodeNames.OperationFailed);
        var match = results[1].Value.ShouldNotBeNull()
            .Deserialize<FlowCorrelationMatchedOutcome<CorrelationMessage>>().ShouldNotBeNull().Match;
        match.Request.Payload.ShouldBe("left");
        match.Response.Payload.ShouldBe("right");
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static FlowMessage Message<T>(T value) => FlowMessage.Create(FlowValue.From(value));

    private static CorrelationNode<CorrelationMessage> CreateNode(
        Func<CorrelationRoutingOptions, CorrelationRoutingOptions>? configure = null,
        TimeProvider? clock = null)
    {
        var options = configure?.Invoke(new CorrelationRoutingOptions())
            ?? new CorrelationRoutingOptions();
        return new CorrelationNode<CorrelationMessage>(
            options,
            input => input.Key,
            input => input.Side,
            clock: clock);
    }
}
