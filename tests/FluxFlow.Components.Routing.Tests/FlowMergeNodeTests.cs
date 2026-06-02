using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class FlowMergeNodeTests
{
    [Fact]
    public async Task Merge_EmitsSourceEnvelopeFromConfiguredInputs()
    {
        var runtimeNode = CreateNode(new
        {
            inputType = "string",
            inputs = new[] { "First", "Second" }
        });
        var first = runtimeNode.FindInput(new PortName("First"))
            .ShouldBeOfType<InputPort<string>>();
        var second = runtimeNode.FindInput(new PortName("Second"))
            .ShouldBeOfType<InputPort<string>>();
        var output = new BufferBlock<FlowMergeItem<string>>();
        LinkOutput(runtimeNode, output);

        await first.Target.SendAsync("one");
        await second.Target.SendAsync("two");
        first.Target.Complete();
        second.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var values = await DrainUntilCompletedAsync(output);
        values.Count.ShouldBe(2);
        values.Select(item => item.Sequence).ShouldBe([1, 2]);
        var bySource = values.ToDictionary(item => item.Source, StringComparer.Ordinal);
        bySource["First"].Value.ShouldBe("one");
        bySource["Second"].Value.ShouldBe("two");
    }

    [Fact]
    public async Task Merge_UsesDefaultLeftAndRightInputs()
    {
        var runtimeNode = CreateNode(new { inputType = "int" });
        var left = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Left))
            .ShouldBeOfType<InputPort<int>>();
        var right = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Right))
            .ShouldBeOfType<InputPort<int>>();
        var output = new BufferBlock<FlowMergeItem<int>>();
        LinkOutput(runtimeNode, output);

        await left.Target.SendAsync(1);
        await right.Target.SendAsync(2);
        left.Target.Complete();
        right.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var values = await DrainUntilCompletedAsync(output);
        values.Select(item => item.Sequence).ShouldBe([1, 2]);
        var bySource = values.ToDictionary(item => item.Source, StringComparer.Ordinal);
        bySource[RoutingComponentPorts.Left].Value.ShouldBe(1);
        bySource[RoutingComponentPorts.Right].Value.ShouldBe(2);
    }

    [Fact]
    public async Task Merge_AssignsSequenceInOutputOrderWhenInputsAreActive()
    {
        var runtimeNode = CreateNode(new
        {
            inputType = "int",
            boundedCapacity = 1
        });
        var left = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Left))
            .ShouldBeOfType<InputPort<int>>();
        var right = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Right))
            .ShouldBeOfType<InputPort<int>>();
        var output = new BufferBlock<FlowMergeItem<int>>();
        LinkOutput(runtimeNode, output);

        var sends = Enumerable.Range(0, 40)
            .Select(value => value % 2 == 0
                ? left.Target.SendAsync(value)
                : right.Target.SendAsync(value));
        await Task.WhenAll(sends);
        left.Target.Complete();
        right.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var values = await DrainUntilCompletedAsync(output);
        values.Count.ShouldBe(40);
        values.Select(item => item.Sequence).ShouldBe(
            Enumerable.Range(1, 40).Select(sequence => (long)sequence));
    }

    [Fact]
    public async Task Merge_WaitsForAllInputsToComplete()
    {
        var runtimeNode = CreateNode(new
        {
            inputType = "string",
            inputs = new[] { "First", "Second" }
        });
        var first = runtimeNode.FindInput(new PortName("First"))
            .ShouldBeOfType<InputPort<string>>();
        var second = runtimeNode.FindInput(new PortName("Second"))
            .ShouldBeOfType<InputPort<string>>();
        var output = new BufferBlock<FlowMergeItem<string>>();
        LinkOutput(runtimeNode, output);

        await first.Target.SendAsync("one");
        first.Target.Complete();
        runtimeNode.Node.Completion.IsCompleted.ShouldBeFalse();
        second.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await DrainUntilCompletedAsync(output)).Single().Value.ShouldBe("one");
    }

    [Fact]
    public async Task Merge_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(new { inputType = "int" });
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);
        var left = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Left))
            .ShouldBeOfType<InputPort<int>>();
        runtimeNode.FindInput(new PortName(RoutingComponentPorts.Right))!.Complete();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Output))!.LinkToDiscard();

        await left.Target.SendAsync(1);
        left.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(RoutingDiagnosticNames.MergeEmitted);
        diagnostic.Attributes["source"].ShouldBe(RoutingComponentPorts.Left);
    }

    [Fact]
    public void Merge_RejectsEmptyInputs()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                inputType = "int",
                inputs = Array.Empty<string>()
            }));

        exception.Message.ShouldContain("inputs");
    }

    [Fact]
    public void Merge_RejectsDuplicateInputs()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                inputType = "int",
                inputs = new[] { "First", "first" }
            }));

        exception.Message.ShouldContain("duplicate");
    }

    [Fact]
    public void Merge_RejectsInvalidInputPort()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                inputType = "int",
                inputs = new[] { "Bad.Port" }
            }));

        exception.Message.ShouldContain("invalid port");
    }

    [Fact]
    public void Merge_RejectsBuiltInInputPort()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                inputType = "int",
                inputs = new[] { RoutingComponentPorts.Output }
            }));

        exception.Message.ShouldContain("built-in port");
    }

    [Fact]
    public void Merge_RejectsUnknownInputType()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                inputType = "missing.type",
                inputs = new[] { "First" }
            }));

        exception.Message.ShouldContain("not registered");
    }

    private static RuntimeNode CreateNode(object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterRoutingComponents(new RecordingExpressionEngine());
        registry.TryGetFactory(RoutingComponentTypes.Merge, out var factory).ShouldBeTrue();
        return factory(RoutingTestHost.CreateContext(RoutingComponentTypes.Merge, configuration));
    }

    private static void LinkOutput<T>(
        RuntimeNode runtimeNode,
        BufferBlock<FlowMergeItem<T>> target)
    {
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<FlowMergeItem<T>>(
                    new PortAddress("test", new NodeName("merge"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
    }

    private static async Task<List<T>> DrainUntilCompletedAsync<T>(
        BufferBlock<T> output)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var values = new List<T>();
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
