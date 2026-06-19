using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class FlowForkNodeTests
{
    [Fact]
    public async Task Fork_EmitsEachInputToConfiguredOutputs_PreservingCorrelation()
    {
        await using var node = new FlowForkNode<string>(
            new ForkRoutingOptions { Outputs = ["First", "Second"] });
        var first = RoutingTestSink.Link(node.Outputs["First"]);
        var second = RoutingTestSink.Link(node.Outputs["Second"]);

        var one = FlowMessage.Create("one");
        var two = FlowMessage.Create("two");
        await node.Input.SendAsync(one);
        await node.Input.SendAsync(two);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var onFirst = await RoutingTestSink.DrainUntilCompletedAsync(first);
        onFirst.Select(m => m.Payload).ShouldBe(["one", "two"]);
        onFirst[0].CorrelationId.ShouldBe(one.CorrelationId);
        (await RoutingTestSink.DrainUntilCompletedAsync(second)).Select(m => m.Payload)
            .ShouldBe(["one", "two"]);
    }

    [Fact]
    public async Task Fork_FirstOutputIsPrimaryOutputPort()
    {
        await using var node = new FlowForkNode<int>(
            new ForkRoutingOptions { Outputs = ["First", "Second"] });

        node.Outputs["First"].ShouldBeSameAs(node.Output);
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<int>>());
        node.Outputs["Second"].LinkTo(DataflowBlock.NullTarget<FlowMessage<int>>());
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Fork_CompletesWithoutInput()
    {
        await using var node = new FlowForkNode<int>(
            new ForkRoutingOptions { Outputs = ["First"] });
        var first = RoutingTestSink.Link(node.Outputs["First"]);

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(first)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Fork_EmitsEvents()
    {
        await using var node = new FlowForkNode<int>(
            new ForkRoutingOptions { Outputs = ["First"] });
        var events = RoutingTestSink.Link(node.Events);
        node.Outputs["First"].LinkTo(DataflowBlock.NullTarget<FlowMessage<int>>());

        await node.Input.SendAsync(FlowMessage.Create(1));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var flowEvent = (await RoutingTestSink.DrainUntilCompletedAsync(events)).ShouldHaveSingleItem();
        flowEvent.Name.ShouldBe(RoutingDiagnosticNames.ForkForwarded);
        flowEvent.Attributes["outputs"].ShouldBe(1);
    }

    [Fact]
    public void Fork_RejectsMissingOutputs()
        => Should.Throw<ArgumentException>(
            () => new FlowForkNode<int>(new ForkRoutingOptions()))
            .Message.ShouldContain("outputs");

    [Fact]
    public void Fork_RejectsDuplicateOutputs()
        => Should.Throw<ArgumentException>(
            () => new FlowForkNode<int>(
                new ForkRoutingOptions { Outputs = ["First", "first"] }))
            .Message.ShouldContain("duplicate");

    [Fact]
    public void Fork_RejectsInvalidOutputPort()
        => Should.Throw<ArgumentException>(
            () => new FlowForkNode<int>(
                new ForkRoutingOptions { Outputs = ["Bad.Port"] }))
            .Message.ShouldContain("invalid port");

    [Fact]
    public void Fork_RejectsBuiltInOutputPort()
        => Should.Throw<ArgumentException>(
            () => new FlowForkNode<int>(
                new ForkRoutingOptions { Outputs = [RoutingComponentPorts.Input] }))
            .Message.ShouldContain("built-in port");

    [Fact]
    public void Fork_RejectsInvalidCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new FlowForkNode<int>(
                new ForkRoutingOptions { Outputs = ["First"], BoundedCapacity = 0 }));

    [Fact]
    public void Fork_RejectsNullOptions()
        => Should.Throw<ArgumentNullException>(() => new FlowForkNode<int>(null!));
}
