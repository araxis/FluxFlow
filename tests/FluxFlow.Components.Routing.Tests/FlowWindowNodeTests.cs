using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Components.Routing.Timing;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class FlowWindowNodeTests
{
    [Fact]
    public async Task Window_EmitsWhenMaxItemsReached()
    {
        var runtimeNode = CreateNode(new
        {
            inputType = "int",
            maxItems = 2,
            boundedCapacity = 8
        });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<int>>();
        var output = new BufferBlock<FlowWindow<int>>();
        LinkOutput(runtimeNode, output);

        await input.Target.SendAsync(10);
        await input.Target.SendAsync(20);
        await input.Target.SendAsync(30);
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var windows = await DrainUntilCompletedAsync(output);
        windows.Count.ShouldBe(2);
        windows[0].Sequence.ShouldBe(1);
        windows[0].Reason.ShouldBe(FlowWindowEmitReason.Count);
        windows[0].Items.ShouldBe([10, 20]);
        windows[1].Sequence.ShouldBe(2);
        windows[1].Reason.ShouldBe(FlowWindowEmitReason.Completion);
        windows[1].Items.ShouldBe([30]);
    }

    [Fact]
    public async Task Window_EmitsWhenTimeElapsedWithoutNextInput()
    {
        var runtimeNode = CreateNode(new
        {
            inputType = "string",
            timeMilliseconds = 25,
            boundedCapacity = 8
        });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<string>>();
        var output = new BufferBlock<FlowWindow<string>>();
        LinkOutput(runtimeNode, output);

        await input.Target.SendAsync("first");
        var window = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        window.Reason.ShouldBe(FlowWindowEmitReason.Time);
        window.Items.ShouldBe(["first"]);
        window.Duration.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public async Task Window_EmitsByCountBeforeTimeLimit()
    {
        var runtimeNode = CreateNode(new
        {
            inputType = "int",
            maxItems = 2,
            timeMilliseconds = 5_000,
            boundedCapacity = 8
        });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<int>>();
        var output = new BufferBlock<FlowWindow<int>>();
        LinkOutput(runtimeNode, output);

        await input.Target.SendAsync(1);
        await input.Target.SendAsync(2);
        var window = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        window.Reason.ShouldBe(FlowWindowEmitReason.Count);
        window.Items.ShouldBe([1, 2]);
    }

    [Fact]
    public async Task Window_CanSuppressPartialWindowOnCompletion()
    {
        var runtimeNode = CreateNode(new
        {
            inputType = "int",
            maxItems = 3,
            emitPartialOnCompletion = false
        });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<int>>();
        var output = new BufferBlock<FlowWindow<int>>();
        LinkOutput(runtimeNode, output);

        await input.Target.SendAsync(1);
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await DrainUntilCompletedAsync(output)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Window_CompletesWithoutInput()
    {
        var runtimeNode = CreateNode(new
        {
            inputType = "int",
            maxItems = 2
        });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<int>>();
        var output = new BufferBlock<FlowWindow<int>>();
        LinkOutput(runtimeNode, output);

        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await DrainUntilCompletedAsync(output)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Window_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(new
        {
            inputType = "int",
            maxItems = 1
        });
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<int>>();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Output))!.LinkToDiscard();

        await input.Target.SendAsync(1);
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(RoutingDiagnosticNames.WindowEmitted);
        diagnostic.Attributes["count"].ShouldBe(1);
        diagnostic.Attributes["reason"].ShouldBe(FlowWindowEmitReason.Count.ToString());
    }

    [Fact]
    public async Task Window_ReportsProcessingFailureAndContinues()
    {
        var clock = new ThrowOnceRoutingClock();
        var node = new FlowWindowNode<int>(
            new WindowRoutingOptions { MaxItems = 1, BoundedCapacity = 8 },
            clock);
        var errors = new BufferBlock<FlowError>();
        var output = new BufferBlock<FlowWindow<int>>();
        node.Errors.LinkTo(errors, new DataflowLinkOptions { PropagateCompletion = true });
        node.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });

        await node.Input.SendAsync(1);
        await node.Input.SendAsync(2);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(RoutingErrorCodes.WindowFailed);
        var window = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        window.Items.ShouldBe([2]);
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
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { inputType = "int" }));

        exception.Message.ShouldContain("maxItems");
    }

    [Fact]
    public void Window_RejectsInvalidCapacity()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                inputType = "int",
                maxItems = 1,
                boundedCapacity = 0
            }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void Window_RejectsUnknownInputType()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                inputType = "missing.type",
                maxItems = 1
            }));

        exception.Message.ShouldContain("not registered");
    }

    private static RuntimeNode CreateNode(object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterRoutingComponents(new RecordingExpressionEngine());
        registry.TryGetFactory(RoutingComponentTypes.Window, out var factory).ShouldBeTrue();
        return factory(RoutingTestHost.CreateContext(RoutingComponentTypes.Window, configuration));
    }

    private static void LinkOutput<T>(
        RuntimeNode runtimeNode,
        BufferBlock<FlowWindow<T>> target)
    {
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<FlowWindow<T>>(
                    new PortAddress("test", new NodeName("windows"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
    }

    private sealed class ThrowOnceRoutingClock : IRoutingClock
    {
        private readonly DateTimeOffset _utcNow = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        private int _calls;

        public DateTimeOffset UtcNow
        {
            get
            {
                if (Interlocked.Increment(ref _calls) == 1)
                {
                    throw new InvalidOperationException("clock failed.");
                }

                return _utcNow;
            }
        }

        public ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
            => new(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
    }

    private static async Task<List<FlowWindow<T>>> DrainUntilCompletedAsync<T>(
        BufferBlock<FlowWindow<T>> output)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var values = new List<FlowWindow<T>>();
        while (await output.OutputAvailableAsync(cancellation.Token))
        {
            while (output.TryReceive(out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }
}
