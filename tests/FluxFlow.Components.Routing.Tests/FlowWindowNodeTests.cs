using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class FlowWindowNodeTests
{
    [Fact]
    public async Task Window_EmitsWhenMaxItemsReached_PreservingOpeningCorrelation()
    {
        await using var node = new FlowWindowNode<int>(
            new WindowRoutingOptions { MaxItems = 2, BoundedCapacity = 8 });
        var output = RoutingTestSink.Link(node.Output);

        var first = FlowMessage.Create(10);
        await node.Input.SendAsync(first);
        await node.Input.SendAsync(FlowMessage.Create(20));
        var third = FlowMessage.Create(30);
        await node.Input.SendAsync(third);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var windows = await RoutingTestSink.DrainUntilCompletedAsync(output);
        windows.Count.ShouldBe(2);
        windows[0].Payload.Sequence.ShouldBe(1);
        windows[0].Payload.Reason.ShouldBe(FlowWindowEmitReason.Count);
        windows[0].Payload.Items.ShouldBe([10, 20]);
        windows[0].CorrelationId.ShouldBe(first.CorrelationId);
        windows[1].Payload.Sequence.ShouldBe(2);
        windows[1].Payload.Reason.ShouldBe(FlowWindowEmitReason.Completion);
        windows[1].Payload.Items.ShouldBe([30]);
        windows[1].CorrelationId.ShouldBe(third.CorrelationId);
    }

    [Fact]
    public async Task Window_EmitsByCountBeforeTimeLimit()
    {
        await using var node = new FlowWindowNode<int>(
            new WindowRoutingOptions { MaxItems = 2, TimeMilliseconds = 5_000, BoundedCapacity = 8 });
        var output = RoutingTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(1));
        await node.Input.SendAsync(FlowMessage.Create(2));
        var window = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        window.Payload.Reason.ShouldBe(FlowWindowEmitReason.Count);
        window.Payload.Items.ShouldBe([1, 2]);
    }

    [Fact]
    public async Task Window_CanSuppressPartialWindowOnCompletion()
    {
        await using var node = new FlowWindowNode<int>(
            new WindowRoutingOptions { MaxItems = 3, EmitPartialOnCompletion = false });
        var output = RoutingTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(1));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(output)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Window_CompletesWithoutInput()
    {
        await using var node = new FlowWindowNode<int>(
            new WindowRoutingOptions { MaxItems = 2 });
        var output = RoutingTestSink.Link(node.Output);

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(output)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Window_EmitsByTimeUsingConfiguredClock()
    {
        var startedAt = DateTimeOffset.Parse("2026-01-01T00:00:02Z");
        var clock = new TrackingFakeTimeProvider(startedAt);
        await using var node = new FlowWindowNode<string>(
            new WindowRoutingOptions { TimeMilliseconds = 25, BoundedCapacity = 8 },
            clock);
        var output = RoutingTestSink.Link(node.Output);

        var timerScheduled = clock.NextTimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create("first"));
        await timerScheduled.WaitAsync(TimeSpan.FromSeconds(30));
        // The time window stays pending until the fake clock is advanced.
        output.TryReceive(out _).ShouldBeFalse();

        clock.Advance(TimeSpan.FromMilliseconds(25));
        var window = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        window.Payload.Reason.ShouldBe(FlowWindowEmitReason.Time);
        window.Payload.Items.ShouldBe(["first"]);
        window.Payload.StartedAt.ShouldBe(startedAt);
        window.Payload.EmittedAt.ShouldBe(startedAt.AddMilliseconds(25));
        window.Payload.Duration.ShouldBe(TimeSpan.FromMilliseconds(25));
    }

    [Fact]
    public async Task Window_EmitsEvents()
    {
        await using var node = new FlowWindowNode<int>(
            new WindowRoutingOptions { MaxItems = 1 });
        var events = RoutingTestSink.Link(node.Events);
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowWindow<int>>>());

        await node.Input.SendAsync(FlowMessage.Create(1));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var emitted = (await RoutingTestSink.DrainUntilCompletedAsync(events)).ShouldHaveSingleItem();
        emitted.Name.ShouldBe(RoutingDiagnosticNames.WindowEmitted);
        emitted.Attributes["count"].ShouldBe(1);
        emitted.Attributes["reason"].ShouldBe(FlowWindowEmitReason.Count.ToString());
    }

    [Fact]
    public async Task Window_ReportsProcessingFailureAndContinues()
    {
        // The clock faults on its first read: opening the window for input 1 throws, is
        // reported as WindowFailed, and the node keeps processing — input 2 opens a new
        // window that emits by count.
        var clock = new ThrowingTimeProvider();
        await using var node = new FlowWindowNode<int>(
            new WindowRoutingOptions { MaxItems = 1, BoundedCapacity = 8 },
            clock);
        var errors = RoutingTestSink.Link(node.Errors);
        var output = RoutingTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(1));
        await node.Input.SendAsync(FlowMessage.Create(2));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var error = (await RoutingTestSink.DrainUntilCompletedAsync(errors)).First();
        error.Code.ShouldBe(RoutingErrorCodes.WindowFailed);
        (await RoutingTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem()
            .Payload.Items.ShouldBe([2]);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Window_DisposeAfterFaultDoesNotThrow()
    {
        var node = new FlowWindowNode<int>(new WindowRoutingOptions { MaxItems = 2 });

        node.Fault(new InvalidOperationException("boom"));
        await node.DisposeAsync();

        node.Completion.IsFaulted.ShouldBeTrue();
    }

    [Fact]
    public void Window_RejectsMissingBoundaries()
        => Should.Throw<ArgumentException>(
            () => new FlowWindowNode<int>(new WindowRoutingOptions()))
            .Message.ShouldContain("maxItems");

    [Fact]
    public void Window_RejectsInvalidCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new FlowWindowNode<int>(
                new WindowRoutingOptions { MaxItems = 1, BoundedCapacity = 0 }));

    [Fact]
    public void Window_RejectsBlankInputType()
        => Should.Throw<ArgumentException>(
            () => new FlowWindowNode<int>(
                new WindowRoutingOptions { InputType = " ", MaxItems = 1 }))
            .Message.ShouldContain("inputType");

    [Fact]
    public void Window_RejectsNullOptions()
        => Should.Throw<ArgumentNullException>(() => new FlowWindowNode<int>(null!));
}
