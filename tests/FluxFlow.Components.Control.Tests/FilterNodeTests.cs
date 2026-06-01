using FluxFlow.Components.Control.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Control.Tests;

public sealed class FilterNodeTests
{
    [Fact]
    public async Task FilterNode_EmitsOnlyMatchingItems()
    {
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(new RecordingExpressionEngine(
                evaluate: (_, context, _) => ((int)context.Variables["input"]!) % 2 == 0)),
            new
            {
                expression = "is-even",
                inputType = "int"
            });
        var input = runtimeNode.FindInput(new PortName(ControlComponentPorts.Input))
            .ShouldBeOfType<InputPort<int>>();
        var output = runtimeNode.FindOutput(new PortName(ControlComponentPorts.Output));
        output.ShouldNotBeNull();
        output.ValueType.ShouldBe(typeof(int));
        var results = new BufferBlock<int>();
        output.TryLinkTo(
            new InputPort<int>(
                new PortAddress("test", new NodeName("results"), new PortName("Input")),
                results),
            propagateCompletion: true,
            out var error);
        error.ShouldBeNull();

        await input.Target.SendAsync(1);
        await input.Target.SendAsync(2);
        await input.Target.SendAsync(3);
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(2);
        results.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task FilterNode_ReportsExpressionFailureAndContinues()
    {
        var calls = 0;
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(new RecordingExpressionEngine(
                evaluate: (_, context, _) =>
                {
                    calls++;
                    if (calls == 1)
                    {
                        throw new InvalidOperationException("predicate failed");
                    }

                    return (int)context.Variables["input"]! > 1;
                })),
            new
            {
                expression = "test",
                expressionName = "filter-test",
                inputType = "int"
            });
        var input = runtimeNode.FindInput(new PortName(ControlComponentPorts.Input))
            .ShouldBeOfType<InputPort<int>>();
        var errors = new BufferBlock<FlowError>();
        var results = new BufferBlock<int>();
        runtimeNode.Node.Errors.LinkTo(errors);
        runtimeNode.FindOutput(new PortName(ControlComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<int>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    results),
                propagateCompletion: true,
                out _);

        await input.Target.SendAsync(1);
        await input.Target.SendAsync(2);
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(ControlErrorCodes.FilterExpressionFailed);
        error.Context!.ShouldContain("expressionName=filter-test");
        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.ShouldBe(2);
    }

    [Fact]
    public async Task FilterNode_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(new RecordingExpressionEngine(
                evaluate: (_, _, _) => true)),
            new
            {
                expression = "pass",
                expressionId = "filter-v1",
                inputType = "string"
        });
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);
        var input = runtimeNode.FindInput(new PortName(ControlComponentPorts.Input))
            .ShouldBeOfType<InputPort<string>>();
        runtimeNode.FindOutput(new PortName(ControlComponentPorts.Output))!
            .LinkToDiscard();

        await input.Target.SendAsync("hello");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(ControlDiagnosticNames.FilterPassed);
        diagnostic.Attributes["inputType"].ShouldBe("string");
        diagnostic.Attributes["expressionId"].ShouldBe("filter-v1");
    }

    private static RuntimeNode CreateNode(
        Action<Options.ControlComponentOptions> configure,
        object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterControlComponents(configure);
        registry.TryGetFactory(ControlComponentTypes.Filter, out var factory).ShouldBeTrue();
        return factory(ControlTestHost.CreateContext(ControlComponentTypes.Filter, configuration));
    }
}
