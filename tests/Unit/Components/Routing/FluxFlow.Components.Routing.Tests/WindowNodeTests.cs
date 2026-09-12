using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class WindowNodeTests
{
    [Fact]
    public async Task Window_EmitsWhenMaxItemsReached_PreservingOpeningCorrelation()
    {
        await using var node = new WindowNode<int>(
            new WindowRoutingOptions { MaxItems = 2, BoundedCapacity = 8 });
        var output = RoutingTestSink.LinkValues<FlowWindow<int>>(node.Output);

        var first = Message(10);
        await node.Input.SendAsync(first);
        await node.Input.SendAsync(Message(20));
        var third = Message(30);
        await node.Input.SendAsync(third);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var windows = await RoutingTestSink.DrainUntilCompletedAsync(output);
        windows.Count.ShouldBe(2);
        windows[0].Value.Sequence.ShouldBe(1);
        windows[0].Value.Reason.ShouldBe(FlowWindowEmitReason.Count);
        windows[0].Value.Items.ShouldBe([10, 20]);
        windows[0].CorrelationId.ShouldBe(first.CorrelationId);
        windows[1].Value.Sequence.ShouldBe(2);
        windows[1].Value.Reason.ShouldBe(FlowWindowEmitReason.Completion);
        windows[1].Value.Items.ShouldBe([30]);
        windows[1].CorrelationId.ShouldBe(third.CorrelationId);
    }

    [Fact]
    public async Task Window_EmitsByCountBeforeTimeLimit()
    {
        await using var node = new WindowNode<int>(
            new WindowRoutingOptions { MaxItems = 2, TimeMilliseconds = 5_000, BoundedCapacity = 8 });
        var output = RoutingTestSink.LinkValues<FlowWindow<int>>(node.Output);

        await node.Input.SendAsync(Message(1));
        await node.Input.SendAsync(Message(2));
        var window = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        window.Value.Reason.ShouldBe(FlowWindowEmitReason.Count);
        window.Value.Items.ShouldBe([1, 2]);
    }

    [Fact]
    public async Task Window_CanSuppressPartialWindowOnCompletion()
    {
        await using var node = new WindowNode<int>(
            new WindowRoutingOptions { MaxItems = 3, EmitPartialOnCompletion = false });
        var output = RoutingTestSink.LinkValues<FlowWindow<int>>(node.Output);

        await node.Input.SendAsync(Message(1));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(output)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Window_CompletesWithoutInput()
    {
        await using var node = new WindowNode<int>(
            new WindowRoutingOptions { MaxItems = 2 });
        var output = RoutingTestSink.LinkValues<FlowWindow<int>>(node.Output);

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await RoutingTestSink.DrainUntilCompletedAsync(output)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Window_EmitsByTimeUsingConfiguredClock()
    {
        var startedAt = DateTimeOffset.Parse("2026-01-01T00:00:02Z");
        var clock = new TrackingFakeTimeProvider(startedAt);
        await using var node = new WindowNode<string>(
            new WindowRoutingOptions { TimeMilliseconds = 25, BoundedCapacity = 8 },
            clock);
        var output = RoutingTestSink.LinkValues<FlowWindow<string>>(node.Output);

        var timerScheduled = clock.NextTimerScheduled;
        await node.Input.SendAsync(Message("first"));
        await timerScheduled.WaitAsync(TimeSpan.FromSeconds(30));
        // The time window stays pending until the fake clock is advanced.
        output.TryReceive(out _).ShouldBeFalse();

        clock.Advance(TimeSpan.FromMilliseconds(25));
        var window = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        window.Value.Reason.ShouldBe(FlowWindowEmitReason.Time);
        window.Value.Items.ShouldBe(["first"]);
        window.Value.StartedAt.ShouldBe(startedAt);
        window.Value.EmittedAt.ShouldBe(startedAt.AddMilliseconds(25));
        window.Value.Duration.ShouldBe(TimeSpan.FromMilliseconds(25));
    }

    [Fact]
    public async Task Window_TimerAndCompletionRaceEmitsPendingWindowExactlyOnce()
    {
        for (var iteration = 0; iteration < 50; iteration++)
        {
            var clock = new TrackingFakeTimeProvider(DateTimeOffset.UnixEpoch);
            await using var node = new WindowNode<int>(
                new WindowRoutingOptions { TimeMilliseconds = 1 },
                clock);
            var output = RoutingTestSink.LinkValues<FlowWindow<int>>(node.Output);
            var scheduled = clock.NextTimerScheduled;
            await node.Input.SendAsync(Message(iteration));
            await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
            using var barrier = new Barrier(2);

            var advance = Task.Run(() =>
            {
                barrier.SignalAndWait();
                clock.Advance(TimeSpan.FromMilliseconds(1));
            });
            var complete = Task.Run(() =>
            {
                barrier.SignalAndWait();
                node.Complete();
            });

            await Task.WhenAll(advance, complete).WaitAsync(TimeSpan.FromSeconds(30));
            await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
            var window = (await RoutingTestSink.DrainUntilCompletedAsync(output))
                .ShouldHaveSingleItem();
            window.Value.Items.ShouldBe([iteration]);
        }
    }

    [Fact]
    public async Task Window_EmitsEvents()
    {
        await using var node = new WindowNode<int>(
            new WindowRoutingOptions { MaxItems = 1 });
        var events = RoutingTestSink.Link(node.Events);
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage>());

        await node.Input.SendAsync(Message(1));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var emitted = (await RoutingTestSink.DrainUntilCompletedAsync(events)).ShouldHaveSingleItem();
        emitted.ToFlowEvent().Name.ShouldBe(RoutingDiagnosticNames.WindowEmitted);
        emitted.ToFlowEvent().Attributes["count"].ShouldBe(1);
        emitted.ToFlowEvent().Attributes["reason"].ShouldBe(FlowWindowEmitReason.Count.ToString());
    }

    [Fact]
    public async Task Window_ReportsProcessingFailureAndContinues()
    {
        // The clock faults on its first read: opening the window for input 1 throws, is
        // reported as WindowFailed, and the node keeps processing — input 2 opens a new
        // window that emits by count.
        var clock = new ThrowingTimeProvider();
        await using var node = new WindowNode<int>(
            new WindowRoutingOptions { MaxItems = 1, BoundedCapacity = 8 },
            clock);
        var output = RoutingTestSink.LinkValues<FlowWindow<int>>(node.Output);

        await node.Input.SendAsync(Message(1));
        await node.Input.SendAsync(Message(2));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var messages = await RoutingTestSink.DrainUntilCompletedAsync(output);
        var error = messages.Single(message => message.IsError).Error.ShouldNotBeNull();
        error.Code.ShouldBe(RoutingErrorCodeNames.OperationFailed);
        error.Details!.Value.GetProperty("legacyCode").GetInt32()
            .ShouldBe(RoutingErrorCodes.WindowFailed);
        messages.Single(message => !message.IsError).Value.Items.ShouldBe([2]);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Window_DisposeAfterFaultDoesNotThrow()
    {
        var node = new WindowNode<int>(new WindowRoutingOptions { MaxItems = 2 });

        node.Fault(new InvalidOperationException("boom"));
        await node.DisposeAsync();

        node.Completion.IsFaulted.ShouldBeTrue();
    }

    [Fact]
    public void Window_RejectsMissingBoundaries()
        => Should.Throw<ArgumentException>(
            () => new WindowNode<int>(new WindowRoutingOptions()))
            .Message.ShouldContain("maxItems");

    [Fact]
    public void Window_RejectsInvalidCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new WindowNode<int>(
                new WindowRoutingOptions { MaxItems = 1, BoundedCapacity = 0 }));

    [Fact]
    public void Window_RejectsNullOptions()
        => Should.Throw<ArgumentNullException>(() => new WindowNode<int>(null!));

    [Fact]
    public async Task Window_runs_as_a_direct_component_with_canonical_request_reply()
    {
        await using var application = new FlowApplication();
        var instance = new WindowNode<int>(new WindowRoutingOptions { MaxItems = 1 });
        var window = application.AddWorkflow("main").Add("window", instance);
        window.Instance.ShouldBeSameAs(instance);
        instance.Ports.Inputs["Input"].ShouldBeSameAs(instance.Input);
        instance.Ports.Outputs["Output"].ShouldBeSameAs(instance.Output);
        application.RepliesFrom(window.Output);
        await application.StartAsync();

        var reply = await application.RequestAsync(window.Input, window.Output,
            Message(42), TimeSpan.FromSeconds(5));

        reply.IsError.ShouldBeFalse();
        reply.Value.Deserialize<FlowWindow<int>>()!.Items.ShouldBe([42]);
        await application.StopAsync();
    }

    [Fact]
    public async Task Window_forwards_the_same_error_envelope_without_materializing_it()
    {
        await using var node = new JsonWindowNode(new WindowRoutingOptions { MaxItems = 1 });
        var output = RoutingTestSink.Link(node.Output);
        var message = Message(1).WithError(new FlowError("input.failed", "Upstream failed.", "test", false));

        (await node.Input.SendAsync(message)).ShouldBeTrue();
        node.Complete();
        var received = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        received.ShouldBeSameAs(message);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Window_reports_invalid_materialization_and_continues_with_valid_input()
    {
        await using var node = new WindowNode<int>(new WindowRoutingOptions { MaxItems = 1 });
        var output = RoutingTestSink.Link(node.Output);
        (await node.Input.SendAsync(Message("not an integer"))).ShouldBeTrue();
        (await node.Input.SendAsync(Message(42))).ShouldBeTrue();
        node.Complete();

        var messages = await RoutingTestSink.DrainUntilCompletedAsync(output);

        messages.Count.ShouldBe(2);
        messages[0].Error!.Code.ShouldBe(RoutingErrorCodeNames.OperationFailed);
        messages[1].IsError.ShouldBeFalse();
        messages[1].Value.ShouldNotBeNull().Deserialize<FlowWindow<int>>()!.Items.ShouldBe([42]);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static FlowMessage Message<T>(T value) => FlowMessage.Create(FlowValue.From(value));
}
