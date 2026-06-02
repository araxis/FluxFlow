using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class FlowForkNodeTests
{
    [Fact]
    public async Task Fork_EmitsEachInputToConfiguredOutputs()
    {
        var runtimeNode = CreateNode(new
        {
            inputType = "string",
            outputs = new[] { "First", "Second" }
        });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<string>>();
        var first = new BufferBlock<string>();
        var second = new BufferBlock<string>();
        LinkOutput(runtimeNode, "First", first);
        LinkOutput(runtimeNode, "Second", second);

        await input.Target.SendAsync("one");
        await input.Target.SendAsync("two");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await DrainUntilCompletedAsync(first)).ShouldBe(["one", "two"]);
        (await DrainUntilCompletedAsync(second)).ShouldBe(["one", "two"]);
    }

    [Fact]
    public async Task Fork_CompletesWithoutInput()
    {
        var runtimeNode = CreateNode(new
        {
            inputType = "int",
            outputs = new[] { "First" }
        });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<int>>();
        var first = new BufferBlock<int>();
        LinkOutput(runtimeNode, "First", first);

        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await DrainUntilCompletedAsync(first)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Fork_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(new
        {
            inputType = "int",
            outputs = new[] { "First" }
        });
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<int>>();
        runtimeNode.FindOutput(new PortName("First"))!.LinkToDiscard();

        await input.Target.SendAsync(1);
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(RoutingDiagnosticNames.ForkForwarded);
        diagnostic.Attributes["outputs"].ShouldBe(1);
    }

    [Fact]
    public void Fork_RejectsMissingOutputs()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { inputType = "int" }));

        exception.Message.ShouldContain("outputs");
    }

    [Fact]
    public void Fork_RejectsDuplicateOutputs()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                inputType = "int",
                outputs = new[] { "First", "first" }
            }));

        exception.Message.ShouldContain("duplicate");
    }

    [Fact]
    public void Fork_RejectsInvalidOutputPort()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                inputType = "int",
                outputs = new[] { "Bad.Port" }
            }));

        exception.Message.ShouldContain("invalid port");
    }

    [Fact]
    public void Fork_RejectsBuiltInOutputPort()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                inputType = "int",
                outputs = new[] { RoutingComponentPorts.Input }
            }));

        exception.Message.ShouldContain("built-in port");
    }

    [Fact]
    public void Fork_RejectsUnknownInputType()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                inputType = "missing.type",
                outputs = new[] { "First" }
            }));

        exception.Message.ShouldContain("not registered");
    }

    private static RuntimeNode CreateNode(object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterRoutingComponents(new RecordingExpressionEngine());
        registry.TryGetFactory(RoutingComponentTypes.Fork, out var factory).ShouldBeTrue();
        return factory(RoutingTestHost.CreateContext(RoutingComponentTypes.Fork, configuration));
    }

    private static void LinkOutput<T>(
        RuntimeNode runtimeNode,
        string port,
        BufferBlock<T> target)
    {
        runtimeNode.FindOutput(new PortName(port))!
            .TryLinkTo(
                new InputPort<T>(
                    new PortAddress("test", new NodeName(port), new PortName("Input")),
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
