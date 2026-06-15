using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Assertions.Tests;

public sealed class FlowAssertionComponentTests
{
    [Fact]
    public async Task FlowAssertionComponent_EmitsResultsAndRoutesInputs()
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
                description = "score-check",
                failureMessage = "Score too low."
            });
        var input = runtimeNode.FindInput(new PortName(AssertionsComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputMessage>>();
        var results = new BufferBlock<FlowAssertionResult>();
        var passed = new BufferBlock<InputMessage>();
        var failed = new BufferBlock<InputMessage>();
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Result))!
            .TryLinkTo(
                new InputPort<FlowAssertionResult>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    results),
                propagateCompletion: true,
                out _);
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Passed))!
            .TryLinkTo(
                new InputPort<InputMessage>(
                    new PortAddress("test", new NodeName("passed"), new PortName("Input")),
                    passed),
                propagateCompletion: true,
                out _);
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Failed))!
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
        first.Description.ShouldBe("score-check");
        first.Message.ShouldBe("Assertion passed.");
        first.Failure.ShouldBeNull();
        second.Passed.ShouldBeFalse();
        second.Message.ShouldBe("Score too low.");
        second.Failure.ShouldNotBeNull();
        (await passed.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Score.ShouldBe(12);
        (await failed.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Score.ShouldBe(3);
    }

    [Fact]
    public async Task FlowAssertionComponent_ReportsExpressionFailureAndContinues()
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
        var input = runtimeNode.FindInput(new PortName(AssertionsComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        var errors = new BufferBlock<FlowError>();
        var results = new BufferBlock<FlowAssertionResult>();
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Errors))!
            .TryLinkTo(
                new InputPort<FlowError>(
                    new PortAddress("test", new NodeName("errors"), new PortName("Input")),
                    errors),
                propagateCompletion: true,
                out _);
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Result))!
            .TryLinkTo(
                new InputPort<FlowAssertionResult>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    results),
                propagateCompletion: true,
                out _);
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Passed))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Failed))!.LinkToDiscard();

        await input.Target.SendAsync("first");
        await input.Target.SendAsync("second");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(AssertionErrorCodes.ExpressionFailed);
        error.Context!.ShouldContain("expressionName=assert-test");
        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task FlowAssertionComponent_CanSuppressRoutedInputs()
    {
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(new RecordingExpressionEngine(
                evaluate: (_, _, _) => true)),
            new
            {
                expression = "assert",
                emitPassedInput = false,
                emitFailedInput = false
            });
        var input = runtimeNode.FindInput(new PortName(AssertionsComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        var results = new BufferBlock<FlowAssertionResult>();
        var passed = new BufferBlock<object>();
        var failed = new BufferBlock<object>();
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Result))!
            .TryLinkTo(
                new InputPort<FlowAssertionResult>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    results),
                propagateCompletion: true,
                out _);
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Passed))!
            .TryLinkTo(
                new InputPort<object>(
                    new PortAddress("test", new NodeName("passed"), new PortName("Input")),
                    passed),
                propagateCompletion: true,
                out _);
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Failed))!
            .TryLinkTo(
                new InputPort<object>(
                    new PortAddress("test", new NodeName("failed"), new PortName("Input")),
                    failed),
                propagateCompletion: true,
                out _);

        await input.Target.SendAsync("value");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Passed.ShouldBeTrue();
        passed.TryReceive(out _).ShouldBeFalse();
        failed.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task FlowAssertionComponent_EmitsDiagnostics()
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
        var input = runtimeNode.FindInput(new PortName(AssertionsComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Result))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Passed))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Failed))!.LinkToDiscard();

        await input.Target.SendAsync("value");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(AssertionDiagnosticNames.Evaluated);
        diagnostic.Attributes["passed"].ShouldBe(true);
        diagnostic.Attributes["expressionId"].ShouldBe("assert-v1");
    }

    [Fact]
    public async Task FlowAssertionComponent_UsesMostSpecificAssignableContextFactory()
    {
        var runtimeNode = CreateNode(
            options => options
                .UseExpressionEngine(new RecordingExpressionEngine(
                    evaluate: (_, context, _) => context.Variables["passed"]))
                .RegisterType<MoreDerivedMessage>("message")
                .UseContextFactory<BaseMessage>(new TestContextFactory<BaseMessage>(passed: false))
                .UseContextFactory<DerivedMessage>(new TestContextFactory<DerivedMessage>(passed: true)),
            new
            {
                expression = "passed",
                inputType = "message"
            });
        var input = runtimeNode.FindInput(new PortName(AssertionsComponentPorts.Input))
            .ShouldBeOfType<InputPort<MoreDerivedMessage>>();
        var results = new BufferBlock<FlowAssertionResult>();
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Result))!
            .TryLinkTo(
                new InputPort<FlowAssertionResult>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    results),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Passed))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Failed))!.LinkToDiscard();

        await input.Target.SendAsync(new MoreDerivedMessage("value"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task FlowAssertionComponent_UsesConfiguredClockForEvaluatedAt()
    {
        var evaluatedAt = new DateTimeOffset(2026, 6, 2, 9, 30, 0, TimeSpan.Zero);
        var runtimeNode = CreateNode(
            options => options
                .UseExpressionEngine(new RecordingExpressionEngine(
                    evaluate: (_, _, _) => true))
                .UseClock(new FakeTimeProvider(evaluatedAt)),
            new
            {
                expression = "assert"
            });
        var input = runtimeNode.FindInput(new PortName(AssertionsComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        var results = new BufferBlock<FlowAssertionResult>();
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Result))!
            .TryLinkTo(
                new InputPort<FlowAssertionResult>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    results),
                propagateCompletion: true,
                out _);
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Passed))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(AssertionsComponentPorts.Failed))!.LinkToDiscard();

        await input.Target.SendAsync("value");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .EvaluatedAt.ShouldBe(evaluatedAt);
    }

    [Fact]
    public void FlowAssertionComponent_RejectsMissingExpression()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                options => options.UseExpressionEngine(new RecordingExpressionEngine()),
                new { }));

        exception.Message.ShouldContain("expression");
    }

    private static RuntimeNode CreateNode(
        Action<Options.AssertionsComponentOptions> configure,
        object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterAssertionsComponents(configure);
        registry.TryGetFactory(AssertionsComponentTypes.Assert, out var factory).ShouldBeTrue();
        return factory(AssertionsTestHost.CreateContext(AssertionsComponentTypes.Assert, configuration));
    }

    private sealed record InputMessage(int Score);

    private record BaseMessage(string Value);

    private record DerivedMessage(string Value) : BaseMessage(Value);

    private sealed record MoreDerivedMessage(string Value) : DerivedMessage(Value);

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

    private sealed class TestContextFactory<TInput>(bool passed) : IFlowMapContextFactory<TInput>
    {
        public FlowMapContext Create(TInput input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["passed"] = passed
                }
            };
    }
}
