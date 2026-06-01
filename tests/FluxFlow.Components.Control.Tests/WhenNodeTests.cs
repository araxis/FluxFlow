using FluxFlow.Components.Control.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Control.Tests;

public sealed class WhenNodeTests
{
    [Fact]
    public async Task WhenNode_RoutesItemsToTrueAndFalseOutputs()
    {
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(new RecordingExpressionEngine(
                evaluate: (_, context, _) => ((int)context.Variables["input"]!) >= 10)),
            new
            {
                expression = "route",
                inputType = "int"
            });
        var input = runtimeNode.FindInput(new PortName(ControlComponentPorts.Input))
            .ShouldBeOfType<InputPort<int>>();
        var trueResults = new BufferBlock<int>();
        var falseResults = new BufferBlock<int>();
        runtimeNode.FindOutput(new PortName(ControlComponentPorts.WhenTrue))!
            .TryLinkTo(
                new InputPort<int>(
                    new PortAddress("test", new NodeName("true"), new PortName("Input")),
                    trueResults),
                propagateCompletion: true,
                out _);
        runtimeNode.FindOutput(new PortName(ControlComponentPorts.WhenFalse))!
            .TryLinkTo(
                new InputPort<int>(
                    new PortAddress("test", new NodeName("false"), new PortName("Input")),
                    falseResults),
                propagateCompletion: true,
                out _);

        await input.Target.SendAsync(5);
        await input.Target.SendAsync(12);
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await falseResults.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(5);
        (await trueResults.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(12);
    }

    [Fact]
    public async Task WhenNode_ReportsExpressionFailureAndContinues()
    {
        var calls = 0;
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(new RecordingExpressionEngine(
                evaluate: (_, _, _) =>
                {
                    calls++;
                    if (calls == 1)
                    {
                        throw new InvalidOperationException("route failed");
                    }

                    return true;
                })),
            new
            {
                expression = "route",
                expressionName = "when-test"
            });
        var input = runtimeNode.FindInput(new PortName(ControlComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        var errors = new BufferBlock<FlowError>();
        var trueResults = new BufferBlock<object>();
        runtimeNode.Node.Errors.LinkTo(errors);
        runtimeNode.FindOutput(new PortName(ControlComponentPorts.WhenTrue))!
            .TryLinkTo(
                new InputPort<object>(
                    new PortAddress("test", new NodeName("true"), new PortName("Input")),
                    trueResults),
                propagateCompletion: true,
                out _);
        runtimeNode.FindOutput(new PortName(ControlComponentPorts.WhenFalse))!
            .LinkToDiscard();

        await input.Target.SendAsync("first");
        await input.Target.SendAsync("second");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(ControlErrorCodes.WhenExpressionFailed);
        error.Context!.ShouldContain("expressionName=when-test");
        (await trueResults.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe("second");
    }

    [Fact]
    public async Task WhenNode_EmitsRouteDiagnostics()
    {
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(new RecordingExpressionEngine(
                evaluate: (_, _, _) => false)),
            new
            {
                expression = "route",
                expressionId = "route-v1"
        });
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);
        var input = runtimeNode.FindInput(new PortName(ControlComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        runtimeNode.FindOutput(new PortName(ControlComponentPorts.WhenTrue))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(ControlComponentPorts.WhenFalse))!.LinkToDiscard();

        await input.Target.SendAsync("value");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(ControlDiagnosticNames.WhenRouted);
        diagnostic.Attributes["route"].ShouldBe(ControlComponentPorts.WhenFalse);
        diagnostic.Attributes["expressionId"].ShouldBe("route-v1");
    }

    private static RuntimeNode CreateNode(
        Action<Options.ControlComponentOptions> configure,
        object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterControlComponents(configure);
        registry.TryGetFactory(ControlComponentTypes.When, out var factory).ShouldBeTrue();
        return factory(ControlTestHost.CreateContext(ControlComponentTypes.When, configuration));
    }
}
