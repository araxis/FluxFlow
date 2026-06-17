using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Sources.Tests;

public sealed class SequenceSourceNodeTests
{
    [Fact]
    public async Task Sequence_EmitsConfiguredValues()
    {
        var runtimeNode = CreateNode(new
        {
            name = "numbers",
            start = 10,
            step = 5,
            count = 3,
            boundedCapacity = 8
        });
        var output = new BufferBlock<SourceSequenceItem>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var items = await DrainUntilCompletedAsync(output);
        items.Select(item => item.Sequence).ShouldBe([1, 2, 3]);
        items.Select(item => item.Value).ShouldBe([10, 15, 20]);
        items.ShouldAllBe(item => item.Name == "numbers");
    }

    [Fact]
    public async Task Sequence_HonorsInitialDelay()
    {
        var runtimeNode = CreateNode(new
        {
            initialDelayMilliseconds = 40,
            count = 1
        });
        var output = new BufferBlock<SourceSequenceItem>();
        LinkOutput(runtimeNode, output);
        var startedAt = DateTimeOffset.UtcNow;

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var item = (await DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        item.Timestamp.ShouldBeGreaterThan(startedAt.AddMilliseconds(20));
    }

    [Fact]
    public async Task Sequence_UsesConfiguredClockForTimingAndTimestamp()
    {
        var startInstant = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startInstant);
        var runtimeNode = CreateNode(
            options => options.UseClock(clock),
            new
            {
                initialDelayMilliseconds = 10,
                intervalMilliseconds = 25,
                count = 2
            });
        var output = new BufferBlock<SourceSequenceItem>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));

        // The node holds a 10ms initial delay then a 25ms interval between the two items.
        // FakeTimeProvider keeps each Task.Delay pending until time advances, so step the
        // clock forward until the run loop completes.
        await AdvanceUntilCompletedAsync(clock, runtimeNode.Node, TimeSpan.FromMilliseconds(25));

        var items = await DrainUntilCompletedAsync(output);
        items.Count.ShouldBe(2);
        // Timestamps come from the configured clock's timeline, not wall-clock time.
        items.ShouldAllBe(item => item.Timestamp >= startInstant);
        items.ShouldAllBe(item => item.Timestamp <= clock.GetUtcNow());
    }

    [Fact]
    public async Task Sequence_CompleteStopsSource()
    {
        var runtimeNode = CreateNode(new
        {
            count = 100,
            intervalMilliseconds = 10
        });
        var output = new BufferBlock<SourceSequenceItem>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        runtimeNode.Node.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        await DrainUntilCompletedAsync(output);
    }

    [Fact]
    public async Task Sequence_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(new { count = 1 });
        var output = new BufferBlock<SourceSequenceItem>();
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        LinkOutput(runtimeNode, output);
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(
                diagnostics,
                new DataflowLinkOptions { PropagateCompletion = true });

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var names = (await DrainDiagnosticsUntilCompletedAsync(diagnostics))
            .Select(diagnostic => diagnostic.Name)
            .ToArray();
        names.ShouldContain(SourceDiagnosticNames.SequenceStarted);
        names.ShouldContain(SourceDiagnosticNames.SequenceEmitted);
        names.ShouldContain(SourceDiagnosticNames.SequenceCompleted);
    }

    [Fact]
    public void Sequence_RejectsInvalidCount()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { count = 0 }));

        exception.Message.ShouldContain("count");
    }

    [Fact]
    public void Sequence_RejectsInvalidStep()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { step = 0 }));

        exception.Message.ShouldContain("step");
    }

    [Fact]
    public void Sequence_RejectsInvalidCapacity()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { boundedCapacity = 0 }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    private static RuntimeNode CreateNode(object configuration)
        => CreateNode(_ => { }, configuration);

    private static RuntimeNode CreateNode(
        Action<Options.SourcesComponentOptions> configure,
        object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterSourcesComponents(configure);
        registry.TryGetFactory(SourcesComponentTypes.Sequence, out var factory).ShouldBeTrue();
        return factory(SourcesTestHost.CreateContext(
            SourcesComponentTypes.Sequence,
            configuration));
    }

    private static void LinkOutput(
        RuntimeNode runtimeNode,
        BufferBlock<SourceSequenceItem> target)
    {
        runtimeNode.FindOutput(new PortName(SourcesComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<SourceSequenceItem>(
                    new PortAddress("test", new NodeName("items"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
    }

    private static async Task<List<T>> DrainUntilCompletedAsync<T>(BufferBlock<T> output)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var items = new List<T>();
        while (await output.OutputAvailableAsync(cancellation.Token))
        {
            while (output.TryReceive(out var item))
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static async Task<List<FlowDiagnostic>> DrainDiagnosticsUntilCompletedAsync(
        BufferBlock<FlowDiagnostic> diagnostics)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
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

    // FakeTimeProvider leaves each Task.Delay pending until time advances, and the run
    // loop registers its delays asynchronously. This drains them deterministically:
    // advance exactly once for each newly created timer, gating on the wrapper's created
    // count (and its registration signal) rather than polling with sleeps. Counting
    // created timers avoids the lost-wakeup where a timer is armed before the test looks.
    private static async Task AdvanceUntilCompletedAsync(
        TrackingFakeTimeProvider clock,
        IFlowNode node,
        TimeSpan step)
    {
        var fired = 0;
        while (!node.Completion.IsCompleted)
        {
            // Capture the next-timer registration signal BEFORE reading the count so a
            // timer armed in the gap is not a lost-wakeup.
            var scheduled = clock.TimerScheduled;

            if (clock.CreatedTimerCount > fired)
            {
                // A delay is armed (or already was) but not yet released: fire it.
                clock.Advance(step);
                fired++;
                continue;
            }

            // No unfired timer yet: wait until the loop arms the next one or completes.
            await Task.WhenAny(scheduled, node.Completion)
                .WaitAsync(TimeSpan.FromSeconds(30));
        }

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
