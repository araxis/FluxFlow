using FluxFlow.Components.Sources.Diagnostics;
using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Sources.Tests;

public sealed class GeneratedSourceNodeTests
{
    [Fact]
    public async Task Generated_EmitsTypedConfiguredItems()
    {
        var runtimeNode = CreateNode(
            options => options.RegisterType<InputMessage>("app.input"),
            new
            {
                outputType = "app.input",
                items = new[]
                {
                    new InputMessage("A-100", 10),
                    new InputMessage("A-101", 20)
                },
                boundedCapacity = 8
            });
        var output = new BufferBlock<InputMessage>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var items = await DrainUntilCompletedAsync(output);
        items.Select(item => item.Id).ShouldBe(["A-100", "A-101"]);
        items.Select(item => item.Value).ShouldBe([10, 20]);
    }

    [Fact]
    public async Task Generated_LoopsUntilMaxItems()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                outputType = "int",
                items = new[] { 1, 2 },
                loop = true,
                maxItems = 5
            });
        var output = new BufferBlock<int>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var items = await DrainUntilCompletedAsync(output);
        items.ShouldBe([1, 2, 1, 2, 1]);
    }

    [Fact]
    public async Task Generated_UsesConfiguredClockForTiming()
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var runtimeNode = CreateNode(
            options => options.UseClock(clock),
            new
            {
                outputType = "string",
                items = new[] { "one", "two" },
                initialDelayMilliseconds = 15,
                intervalMilliseconds = 30
            });
        var output = new BufferBlock<string>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));

        // The node holds a 15ms initial delay and then a 30ms interval delay between the
        // two items. FakeTimeProvider keeps each Task.Delay pending until time advances,
        // so step the clock forward until the run loop drains and completes.
        await AdvanceUntilCompletedAsync(clock, runtimeNode.Node, TimeSpan.FromMilliseconds(30));

        (await DrainUntilCompletedAsync(output)).ShouldBe(["one", "two"]);
    }

    [Fact]
    public async Task Generated_CompletesEmptyItemList()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                outputType = "string",
                items = Array.Empty<string>()
            });
        var output = new BufferBlock<string>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await DrainUntilCompletedAsync(output)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Generated_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                name = "demo",
                outputType = "string",
                items = new[] { "one" }
            });
        var output = new BufferBlock<string>();
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
        names.ShouldContain(SourceDiagnosticNames.GeneratedStarted);
        names.ShouldContain(SourceDiagnosticNames.GeneratedEmitted);
        names.ShouldContain(SourceDiagnosticNames.GeneratedCompleted);
    }

    [Fact]
    public async Task Generated_CompleteBeforeStartCompletesOutput()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                outputType = "string",
                items = new[] { "one" }
            });
        var output = new BufferBlock<string>();
        LinkOutput(runtimeNode, output);

        runtimeNode.Node.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        await output.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Generated_StartAfterCompleteDoesNotEmit()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                outputType = "string",
                items = new[] { "one" }
            });
        var output = new BufferBlock<string>();
        LinkOutput(runtimeNode, output);

        runtimeNode.Node.Complete();
        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await DrainUntilCompletedAsync(output)).ShouldBeEmpty();
    }

    [Fact]
    public void Generated_ConstructorRejectsLoopWithoutMaxItems()
    {
        var options = new GeneratedSourceOptions { Loop = true };

        var exception = Should.Throw<ArgumentException>(
            () => new GeneratedSourceNode<string>(options, ["one"]));

        exception.Message.ShouldContain("maxItems");
    }

    [Fact]
    public void Generated_RejectsMissingItems()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(_ => { }, new { outputType = "string" }));

        exception.Message.ShouldContain("items");
    }

    [Fact]
    public void Generated_RejectsUnregisteredType()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    outputType = "missing.type",
                    items = new[] { "one" }
                }));

        exception.Message.ShouldContain("not registered");
    }

    [Fact]
    public void Generated_RejectsLoopWithoutMaxItems()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    outputType = "int",
                    items = new[] { 1 },
                    loop = true
                }));

        exception.Message.ShouldContain("maxItems");
    }

    [Fact]
    public void Generated_RejectsInvalidItemConversion()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    outputType = "int",
                    items = new[] { "not-number" }
                }));

        exception.Message.ShouldContain("could not be converted");
    }

    private static RuntimeNode CreateNode(
        Action<Options.SourcesComponentOptions> configure,
        object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterSourcesComponents(configure);
        registry.TryGetFactory(SourcesComponentTypes.Generated, out var factory).ShouldBeTrue();
        return factory(SourcesTestHost.CreateContext(
            SourcesComponentTypes.Generated,
            configuration));
    }

    private static void LinkOutput<T>(
        RuntimeNode runtimeNode,
        BufferBlock<T> target)
    {
        runtimeNode.FindOutput(new PortName(SourcesComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<T>(
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
            if (clock.CreatedTimerCount > fired)
            {
                // A delay is armed (or already was) but not yet released: fire it.
                clock.Advance(step);
                fired++;
                continue;
            }

            // No unfired timer yet: wait until the loop arms the next one or completes.
            var scheduled = clock.TimerScheduled;
            await Task.WhenAny(scheduled, node.Completion)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed record InputMessage(string Id, int Value);
}
