using FluxFlow.Components.Routing;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

// Every test news the node directly with its options and a route-key selector — no engine,
// no registry. Messages travel as FlowMessage<T>; the correlation id flows input -> matched
// / default / route ports and onto any error for free.
public sealed class FlowSwitchNodeTests
{
    private sealed record InputMessage(string Id, string Category);

    [Fact]
    public async Task Switch_RoutesMatchedAndDefaultInputs_PreservingCorrelation()
    {
        await using var node = new FlowSwitchNode<InputMessage>(
            new SwitchRoutingOptions { Routes = ["priority"], DefaultRoute = "other" },
            input => input.Category);
        var matched = RoutingTestSink.Link(node.Matched);
        var defaults = RoutingTestSink.Link(node.Default);

        var first = FlowMessage.Create(new InputMessage("A-100", "priority"));
        var second = FlowMessage.Create(new InputMessage("A-101", "standard"));
        await node.Input.SendAsync(first);
        await node.Input.SendAsync(second);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var routedMatched = (await RoutingTestSink.DrainUntilCompletedAsync(matched)).ShouldHaveSingleItem();
        routedMatched.Payload.Id.ShouldBe("A-100");
        routedMatched.CorrelationId.ShouldBe(first.CorrelationId);

        var routedDefault = (await RoutingTestSink.DrainUntilCompletedAsync(defaults)).ShouldHaveSingleItem();
        routedDefault.Payload.Id.ShouldBe("A-101");
        routedDefault.CorrelationId.ShouldBe(second.CorrelationId);
    }

    [Fact]
    public async Task Switch_TreatsAnyNonEmptyRouteAsMatchedWhenRoutesAreEmpty()
    {
        await using var node = new FlowSwitchNode<string>(
            new SwitchRoutingOptions(),
            _ => "dynamic");
        var matched = RoutingTestSink.Link(node.Matched);
        node.Default.LinkTo(DataflowBlock.NullTarget<FlowMessage<string>>());

        await node.Input.SendAsync(FlowMessage.Create("value"));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(matched)).ShouldHaveSingleItem()
            .Payload.ShouldBe("value");
    }

    [Fact]
    public async Task Switch_SupportsCaseInsensitiveRouteMatching()
    {
        await using var node = new FlowSwitchNode<string>(
            new SwitchRoutingOptions { Routes = ["priority"], CaseSensitive = false },
            _ => "PRIORITY");
        var matched = RoutingTestSink.Link(node.Matched);
        node.Default.LinkTo(DataflowBlock.NullTarget<FlowMessage<string>>());

        await node.Input.SendAsync(FlowMessage.Create("value"));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(matched)).ShouldHaveSingleItem()
            .Payload.ShouldBe("value");
    }

    [Fact]
    public async Task Switch_CanSuppressRoutedInputs()
    {
        await using var node = new FlowSwitchNode<string>(
            new SwitchRoutingOptions
            {
                Routes = ["priority"],
                EmitMatchedInput = false,
                EmitDefaultInput = false
            },
            _ => "priority");
        var matched = RoutingTestSink.Link(node.Matched);
        var defaults = RoutingTestSink.Link(node.Default);

        await node.Input.SendAsync(FlowMessage.Create("value"));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(matched)).ShouldBeEmpty();
        (await RoutingTestSink.DrainUntilCompletedAsync(defaults)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Switch_EmitsConfiguredRouteOutputPorts_PreservingCorrelation()
    {
        await using var node = new FlowSwitchNode<InputMessage>(
            new SwitchRoutingOptions
            {
                Routes = ["priority", "standard"],
                RouteOutputs = new Dictionary<string, string>
                {
                    ["priority"] = "Priority",
                    ["standard"] = "Standard"
                }
            },
            input => input.Category);
        var priority = RoutingTestSink.Link(node.RouteOutputs["Priority"]);
        var standard = RoutingTestSink.Link(node.RouteOutputs["Standard"]);
        node.Matched.LinkTo(DataflowBlock.NullTarget<FlowMessage<InputMessage>>());
        node.Default.LinkTo(DataflowBlock.NullTarget<FlowMessage<InputMessage>>());

        var first = FlowMessage.Create(new InputMessage("A-100", "priority"));
        var second = FlowMessage.Create(new InputMessage("A-101", "standard"));
        await node.Input.SendAsync(first);
        await node.Input.SendAsync(second);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var onPriority = (await RoutingTestSink.DrainUntilCompletedAsync(priority)).ShouldHaveSingleItem();
        onPriority.Payload.Id.ShouldBe("A-100");
        onPriority.CorrelationId.ShouldBe(first.CorrelationId);
        (await RoutingTestSink.DrainUntilCompletedAsync(standard)).ShouldHaveSingleItem()
            .Payload.Id.ShouldBe("A-101");
    }

    [Fact]
    public async Task Switch_EmitsRouteEnvelopeWhenEnabled()
    {
        await using var node = new FlowSwitchNode<InputMessage>(
            new SwitchRoutingOptions
            {
                Routes = ["priority"],
                DefaultRoute = "other",
                EmitRouteEnvelope = true,
                RouteOutputs = new Dictionary<string, string> { ["priority"] = "Priority" }
            },
            input => input.Category);
        node.Routed.ShouldNotBeNull();
        var routed = RoutingTestSink.Link(node.Routed!);
        node.Matched.LinkTo(DataflowBlock.NullTarget<FlowMessage<InputMessage>>());
        node.Default.LinkTo(DataflowBlock.NullTarget<FlowMessage<InputMessage>>());
        node.RouteOutputs["Priority"].LinkTo(DataflowBlock.NullTarget<FlowMessage<InputMessage>>());

        var first = FlowMessage.Create(new InputMessage("A-100", "priority"));
        var second = FlowMessage.Create(new InputMessage("A-101", "standard"));
        await node.Input.SendAsync(first);
        await node.Input.SendAsync(second);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var envelopes = await RoutingTestSink.DrainUntilCompletedAsync(routed);
        envelopes.Count.ShouldBe(2);
        envelopes[0].Payload.Id.ShouldBe("A-100");
        envelopes[0].CorrelationId.ShouldBe(first.CorrelationId);
        envelopes[1].Payload.Id.ShouldBe("A-101");
        envelopes[1].CorrelationId.ShouldBe(second.CorrelationId);
    }

    [Fact]
    public async Task Switch_RouteEnvelopePortIsNullWhenDisabled()
    {
        await using var node = new FlowSwitchNode<string>(
            new SwitchRoutingOptions { Routes = ["priority"] },
            _ => "priority");

        node.Routed.ShouldBeNull();
        node.Matched.LinkTo(DataflowBlock.NullTarget<FlowMessage<string>>());
        node.Default.LinkTo(DataflowBlock.NullTarget<FlowMessage<string>>());
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Switch_CanMapSeveralRoutesToTheSameOutputPort()
    {
        var calls = 0;
        await using var node = new FlowSwitchNode<string>(
            new SwitchRoutingOptions
            {
                Routes = ["priority", "urgent"],
                RouteOutputs = new Dictionary<string, string>
                {
                    ["priority"] = "Important",
                    ["urgent"] = "Important"
                }
            },
            _ => ++calls == 1 ? "priority" : "urgent");
        var important = RoutingTestSink.Link(node.RouteOutputs["Important"]);
        node.Matched.LinkTo(DataflowBlock.NullTarget<FlowMessage<string>>());
        node.Default.LinkTo(DataflowBlock.NullTarget<FlowMessage<string>>());

        await node.Input.SendAsync(FlowMessage.Create("first"));
        await node.Input.SendAsync(FlowMessage.Create("second"));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(important)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Switch_ReportsExpressionFailureAndContinues()
    {
        var calls = 0;
        await using var node = new FlowSwitchNode<string>(
            new SwitchRoutingOptions { ExpressionName = "switch-test" },
            _ =>
            {
                if (++calls == 1)
                {
                    throw new InvalidOperationException("switch failed");
                }

                return "ok";
            });
        var matched = RoutingTestSink.Link(node.Matched);
        var errors = RoutingTestSink.Link(node.Errors);
        node.Default.LinkTo(DataflowBlock.NullTarget<FlowMessage<string>>());

        var first = FlowMessage.Create("first");
        await node.Input.SendAsync(first);
        await node.Input.SendAsync(FlowMessage.Create("second"));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var error = (await RoutingTestSink.DrainUntilCompletedAsync(errors)).ShouldHaveSingleItem();
        error.Code.ShouldBe(RoutingErrorCodes.SwitchExpressionFailed);
        error.CorrelationId.ShouldBe(first.CorrelationId);
        error.Context!.ShouldContain("expressionName=switch-test");
        (await RoutingTestSink.DrainUntilCompletedAsync(matched)).ShouldHaveSingleItem()
            .Payload.ShouldBe("second");
    }

    [Fact]
    public async Task Switch_EmitsEventsWithRouteKey()
    {
        await using var node = new FlowSwitchNode<string>(
            new SwitchRoutingOptions { Routes = ["priority"], ExpressionId = "route-v1" },
            _ => "priority");
        var events = RoutingTestSink.Link(node.Events);
        node.Matched.LinkTo(DataflowBlock.NullTarget<FlowMessage<string>>());
        node.Default.LinkTo(DataflowBlock.NullTarget<FlowMessage<string>>());

        await node.Input.SendAsync(FlowMessage.Create("value"));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var routedEvent = (await RoutingTestSink.DrainUntilCompletedAsync(events))
            .ShouldHaveSingleItem();
        routedEvent.Name.ShouldBe(Diagnostics.RoutingDiagnosticNames.SwitchRouted);
        routedEvent.Attributes["routeKey"].ShouldBe("priority");
        routedEvent.Attributes["matched"].ShouldBe(true);
        routedEvent.Attributes["expressionId"].ShouldBe("route-v1");
    }

    [Fact]
    public async Task Switch_OutputFansOutToManyConsumers()
    {
        await using var node = new FlowSwitchNode<string>(
            new SwitchRoutingOptions { Routes = ["go"] },
            _ => "go");
        var logger = RoutingTestSink.Link(node.Matched);
        var mapper = RoutingTestSink.Link(node.Matched);
        node.Default.LinkTo(DataflowBlock.NullTarget<FlowMessage<string>>());

        await node.Input.SendAsync(FlowMessage.Create("a"));
        await node.Input.SendAsync(FlowMessage.Create("b"));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(logger)).Select(m => m.Payload).ShouldBe(["a", "b"]);
        (await RoutingTestSink.DrainUntilCompletedAsync(mapper)).Select(m => m.Payload).ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task Switch_DisposeAfterFaultDoesNotThrow()
    {
        var node = new FlowSwitchNode<string>(new SwitchRoutingOptions(), _ => "x");

        node.Fault(new InvalidOperationException("boom"));
        await node.DisposeAsync();

        node.Completion.IsFaulted.ShouldBeTrue();
    }

    [Fact]
    public void Switch_RejectsNullOptions()
        => Should.Throw<ArgumentNullException>(
            () => new FlowSwitchNode<string>(null!, _ => "x"));

    [Fact]
    public void Switch_RejectsNullSelector()
        => Should.Throw<ArgumentNullException>(
            () => new FlowSwitchNode<string>(new SwitchRoutingOptions(), null!));

    [Fact]
    public void Switch_RejectsInvalidCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new FlowSwitchNode<string>(
                new SwitchRoutingOptions { BoundedCapacity = 0 },
                _ => "x"));

    [Fact]
    public void Switch_RejectsBlankInputType()
        => Should.Throw<ArgumentException>(
            () => new FlowSwitchNode<string>(
                new SwitchRoutingOptions { InputType = " " },
                _ => "x"))
            .Message.ShouldContain("inputType");

    [Fact]
    public async Task Switch_UsesConfiguredClockForEventTimestamp()
    {
        var timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        await using var node = new FlowSwitchNode<string>(
            new SwitchRoutingOptions { Routes = ["matched"] },
            _ => "matched",
            clock: new FakeTimeProvider(timestamp));
        var events = RoutingTestSink.Link(node.Events);
        node.Matched.LinkTo(DataflowBlock.NullTarget<FlowMessage<string>>());
        node.Default.LinkTo(DataflowBlock.NullTarget<FlowMessage<string>>());

        await node.Input.SendAsync(FlowMessage.Create("value"));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(events)).ShouldHaveSingleItem()
            .Timestamp.ShouldBe(timestamp);
    }
}
