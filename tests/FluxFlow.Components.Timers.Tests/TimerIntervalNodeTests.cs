using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class TimerIntervalNodeTests
{
    [Fact]
    public async Task Interval_EmitsConfiguredTickCount()
    {
        var runtimeNode = CreateNode(new
        {
            name = "poll",
            intervalMilliseconds = 10,
            emitImmediately = true,
            maxTicks = 3,
            boundedCapacity = 8
        });
        var output = new BufferBlock<TimerTick>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var ticks = await DrainUntilCompletedAsync(output);
        ticks.Select(tick => tick.Sequence).ShouldBe([1, 2, 3]);
        ticks.ShouldAllBe(tick => tick.Name == "poll");
        ticks.ShouldAllBe(tick => tick.Interval == TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public async Task Interval_HonorsInitialDelay()
    {
        var runtimeNode = CreateNode(new
        {
            intervalMilliseconds = 100,
            initialDelayMilliseconds = 40,
            maxTicks = 1
        });
        var output = new BufferBlock<TimerTick>();
        LinkOutput(runtimeNode, output);
        var startedAt = DateTimeOffset.UtcNow;

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var tick = (await DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        tick.Timestamp.ShouldBeGreaterThan(startedAt.AddMilliseconds(20));
        tick.Sequence.ShouldBe(1);
    }

    [Fact]
    public async Task Interval_CompleteStopsTimer()
    {
        var runtimeNode = CreateNode(new
        {
            intervalMilliseconds = 10,
            emitImmediately = true,
            boundedCapacity = 8
        });
        var output = new BufferBlock<TimerTick>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        runtimeNode.Node.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        output.Completion.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Interval_DisposeBeforeStartCompletesOutput()
    {
        var runtimeNode = CreateNode(new
        {
            intervalMilliseconds = 10
        });
        var output = new BufferBlock<TimerTick>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.ShouldBeAssignableTo<IAsyncDisposable>()!
            .DisposeAsync();

        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        output.Completion.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Interval_EmitsDiagnosticsAndEvents()
    {
        var runtimeNode = CreateNode(new
        {
            name = "diag",
            intervalMilliseconds = 10,
            emitImmediately = true,
            maxTicks = 1
        });
        var output = new BufferBlock<TimerTick>();
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        var events = new BufferBlock<FlowEvent>();
        LinkOutput(runtimeNode, output);
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(
                diagnostics,
                new DataflowLinkOptions { PropagateCompletion = true });
        runtimeNode.Node.ShouldBeAssignableTo<IFlowEventSource>()!
            .Events.LinkTo(
                events,
                new DataflowLinkOptions { PropagateCompletion = true });

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var diagnosticNames = (await DrainDiagnosticsUntilCompletedAsync(diagnostics))
            .Select(diagnostic => diagnostic.Name)
            .ToArray();
        diagnosticNames.ShouldContain(TimerDiagnosticNames.IntervalStarted);
        diagnosticNames.ShouldContain(TimerDiagnosticNames.IntervalTick);
        diagnosticNames.ShouldContain(TimerDiagnosticNames.IntervalStopped);
        var flowEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        flowEvent.Type.ShouldBe(TimerEventNames.IntervalTick);
        flowEvent.Subject.ShouldBe("diag");
    }

    [Fact]
    public async Task Interval_RejectsSecondStart()
    {
        var runtimeNode = CreateNode(new
        {
            intervalMilliseconds = 50,
            emitImmediately = true,
            maxTicks = 1
        });
        runtimeNode.FindOutput(new PortName(TimerComponentPorts.Output))!
            .LinkToDiscard();

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => runtimeNode.Node.StartAsync());
        exception.Message.ShouldContain("already started");
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Interval_RejectsMissingInterval()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { }));

        exception.Message.ShouldContain("interval");
    }

    [Fact]
    public void Interval_RejectsInvalidInterval()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { intervalMilliseconds = 0 }));

        exception.Message.ShouldContain("interval");
    }

    [Fact]
    public void Interval_RejectsInvalidInitialDelay()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                intervalMilliseconds = 10,
                initialDelayMilliseconds = -1
            }));

        exception.Message.ShouldContain("initialDelay");
    }

    [Fact]
    public void Interval_RejectsInvalidMaxTicks()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                intervalMilliseconds = 10,
                maxTicks = 0
            }));

        exception.Message.ShouldContain("maxTicks");
    }

    [Fact]
    public void Interval_RejectsInvalidBoundedCapacity()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                intervalMilliseconds = 10,
                boundedCapacity = 0
            }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    private static RuntimeNode CreateNode(object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterTimerComponents();
        registry.TryGetFactory(TimerComponentTypes.Interval, out var factory).ShouldBeTrue();
        return factory(TimerTestHost.CreateContext(
            TimerComponentTypes.Interval,
            configuration));
    }

    private static void LinkOutput(RuntimeNode runtimeNode, BufferBlock<TimerTick> target)
    {
        runtimeNode.FindOutput(new PortName(TimerComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<TimerTick>(
                    new PortAddress("test", new NodeName("ticks"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
    }

    private static async Task<List<TimerTick>> DrainUntilCompletedAsync(
        BufferBlock<TimerTick> output)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ticks = new List<TimerTick>();
        while (await output.OutputAvailableAsync(cancellation.Token))
        {
            while (output.TryReceive(out var tick))
            {
                ticks.Add(tick);
            }
        }

        return ticks;
    }

    private static async Task<List<FlowDiagnostic>> DrainDiagnosticsUntilCompletedAsync(
        BufferBlock<FlowDiagnostic> diagnostics)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entries = new List<FlowDiagnostic>();
        while (await diagnostics.OutputAvailableAsync(cancellation.Token))
        {
            while (diagnostics.TryReceive(out var entry))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }
}
