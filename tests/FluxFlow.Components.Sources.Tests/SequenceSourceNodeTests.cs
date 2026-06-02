using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Diagnostics;
using FluxFlow.Components.Sources.Timing;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
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

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

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

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var item = (await DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        item.Timestamp.ShouldBeGreaterThan(startedAt.AddMilliseconds(20));
    }

    [Fact]
    public async Task Sequence_UsesConfiguredClockForTimingAndTimestamp()
    {
        var clock = new RecordingSourceClock
        {
            UtcNow = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero)
        };
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

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var items = await DrainUntilCompletedAsync(output);
        items.Count.ShouldBe(2);
        items.ShouldAllBe(item => item.Timestamp == clock.UtcNow);
        clock.Delays.ShouldBe([TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(25)]);
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

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        runtimeNode.Node.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

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

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

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
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
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

    private sealed class RecordingSourceClock : ISourceClock
    {
        public DateTimeOffset UtcNow { get; init; }

        public List<TimeSpan> Delays { get; } = [];

        public ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(delay);
            return ValueTask.CompletedTask;
        }
    }
}
