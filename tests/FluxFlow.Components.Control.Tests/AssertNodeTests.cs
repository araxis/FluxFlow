using FluxFlow.Components.Control.Contracts;
using FluxFlow.Components.Control.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Control.Tests;

public sealed class AssertNodeTests
{
    [Fact]
    public async Task AssertNode_EmitsResultsAndRoutesInputs()
    {
        var runtimeNode = CreateNode(
            options => options
                .UseExpressionEngine(new RecordingExpressionEngine(
                    evaluate: (_, context, _) => (int)context.Variables["score"]! >= 10))
                .RegisterType<InputMessage>("app.input")
                .UseContextFactory(new InputMessageContextFactory()),
            new
            {
                expression = "score >= 10",
                inputType = "app.input",
                name = "score-check",
                failureMessage = "Score too low."
            });
        var input = runtimeNode.FindInput(new PortName(ControlComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputMessage>>();
        var results = new BufferBlock<ControlAssertionResult>();
        var passed = new BufferBlock<InputMessage>();
        var failed = new BufferBlock<InputMessage>();
        runtimeNode.FindOutput(new PortName(ControlComponentPorts.Result))!
            .TryLinkTo(
                new InputPort<ControlAssertionResult>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    results),
                propagateCompletion: true,
                out _);
        runtimeNode.FindOutput(new PortName(ControlComponentPorts.Passed))!
            .TryLinkTo(
                new InputPort<InputMessage>(
                    new PortAddress("test", new NodeName("passed"), new PortName("Input")),
                    passed),
                propagateCompletion: true,
                out _);
        runtimeNode.FindOutput(new PortName(ControlComponentPorts.Failed))!
            .TryLinkTo(
                new InputPort<InputMessage>(
                    new PortAddress("test", new NodeName("failed"), new PortName("Input")),
                    failed),
                propagateCompletion: true,
                out _);

        await input.Target.SendAsync(new InputMessage(12));
        await input.Target.SendAsync(new InputMessage(3));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var first = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        first.Passed.ShouldBeTrue();
        first.Name.ShouldBe("score-check");
        second.Passed.ShouldBeFalse();
        second.Message.ShouldBe("Score too low.");
        (await passed.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Score.ShouldBe(12);
        (await failed.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Score.ShouldBe(3);
    }

    [Fact]
    public async Task AssertNode_ReportsExpressionFailureAndContinues()
    {
        var calls = 0;
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(new RecordingExpressionEngine(
                evaluate: (_, _, _) =>
                {
                    calls++;
                    if (calls == 1)
                    {
                        throw new InvalidOperationException("assert failed");
                    }

                    return true;
                })),
            new
            {
                expression = "assert",
                expressionName = "assert-test"
            });
        var input = runtimeNode.FindInput(new PortName(ControlComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        var errors = new BufferBlock<FlowError>();
        var results = new BufferBlock<ControlAssertionResult>();
        runtimeNode.Node.Errors.LinkTo(errors);
        runtimeNode.FindOutput(new PortName(ControlComponentPorts.Result))!
            .TryLinkTo(
                new InputPort<ControlAssertionResult>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    results),
                propagateCompletion: true,
                out _);
        runtimeNode.FindOutput(new PortName(ControlComponentPorts.Passed))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(ControlComponentPorts.Failed))!.LinkToDiscard();

        await input.Target.SendAsync("first");
        await input.Target.SendAsync("second");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(ControlErrorCodes.AssertExpressionFailed);
        error.Context!.ShouldContain("expressionName=assert-test");
        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task AssertNode_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(new RecordingExpressionEngine(
                evaluate: (_, _, _) => true)),
            new
            {
                expression = "assert",
                expressionId = "assert-v1"
        });
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);
        var input = runtimeNode.FindInput(new PortName(ControlComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        runtimeNode.FindOutput(new PortName(ControlComponentPorts.Result))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(ControlComponentPorts.Passed))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(ControlComponentPorts.Failed))!.LinkToDiscard();

        await input.Target.SendAsync("value");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(ControlDiagnosticNames.AssertEvaluated);
        diagnostic.Attributes["passed"].ShouldBe(true);
        diagnostic.Attributes["expressionId"].ShouldBe("assert-v1");
    }

    [Fact]
    public void AssertNode_RejectsMissingExpression()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                options => options.UseExpressionEngine(new RecordingExpressionEngine()),
                new { }));

        exception.Message.ShouldContain("expression");
    }

    private static RuntimeNode CreateNode(
        Action<Options.ControlComponentOptions> configure,
        object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterControlComponents(configure);
        registry.TryGetFactory(ControlComponentTypes.Assert, out var factory).ShouldBeTrue();
        return factory(ControlTestHost.CreateContext(ControlComponentTypes.Assert, configuration));
    }

    private sealed record InputMessage(int Score);

    private sealed class InputMessageContextFactory : IFlowMapContextFactory<InputMessage>
    {
        public FlowMapContext Create(InputMessage input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["score"] = input.Score
                }
            };
    }
}
