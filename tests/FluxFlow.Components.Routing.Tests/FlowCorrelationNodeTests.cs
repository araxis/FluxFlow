using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class FlowCorrelationNodeTests
{
    [Fact]
    public async Task Correlation_MatchesRequestAndResponseByKey()
    {
        var runtimeNode = CreateNode(new { });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<CorrelationMessage>>();
        runtimeNode.FindInput(new PortName(RoutingComponentPorts.Request)).ShouldBeNull();
        runtimeNode.FindInput(new PortName(RoutingComponentPorts.Response)).ShouldBeNull();
        var matched = new BufferBlock<FlowCorrelationMatch<CorrelationMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Matched, matched);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Timeouts))!.LinkToDiscard();

        await input.Target.SendAsync(new CorrelationMessage("A-100", "request", "start"));
        await input.Target.SendAsync(new CorrelationMessage("A-100", "response", "done"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var match = await matched.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        match.Key.ShouldBe("A-100");
        match.Request.Payload.ShouldBe("start");
        match.Response.Payload.ShouldBe("done");
        match.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task Correlation_MatchesSplitRequestAndResponseInputs()
    {
        var runtimeNode = CreateSplitNode(new { });
        runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input)).ShouldBeNull();
        var request = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Request))
            .ShouldBeOfType<InputPort<CorrelationMessage>>();
        var response = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Response))
            .ShouldBeOfType<InputPort<CorrelationMessage>>();
        var matched = new BufferBlock<FlowCorrelationMatch<CorrelationMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Matched, matched);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Timeouts))!.LinkToDiscard();

        await response.Target.SendAsync(new CorrelationMessage("A-100", "ignored", "done"));
        await request.Target.SendAsync(new CorrelationMessage("A-100", "ignored", "start"));
        request.Target.Complete();
        response.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var match = await matched.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        match.Key.ShouldBe("A-100");
        match.Request.Payload.ShouldBe("start");
        match.Response.Payload.ShouldBe("done");
    }

    [Fact]
    public async Task Correlation_EmitsSplitInputTimeoutsOnCompletion()
    {
        var runtimeNode = CreateSplitNode(new { timeoutMilliseconds = 10 });
        var request = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Request))
            .ShouldBeOfType<InputPort<CorrelationMessage>>();
        var response = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Response))
            .ShouldBeOfType<InputPort<CorrelationMessage>>();
        var timeouts = new BufferBlock<FlowCorrelationTimeout<CorrelationMessage>>();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Matched))!.LinkToDiscard();
        LinkOutput(runtimeNode, RoutingComponentPorts.Timeouts, timeouts);

        await request.Target.SendAsync(new CorrelationMessage("A-100", "ignored", "start"));
        request.Target.Complete();
        response.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var timeout = await timeouts.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        timeout.Key.ShouldBe("A-100");
        timeout.Side.ShouldBe("request");
        timeout.Value.Payload.ShouldBe("start");
    }

    [Fact]
    public async Task Correlation_MatchesOutOfOrderResponseAndRequest()
    {
        var runtimeNode = CreateNode(new { });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<CorrelationMessage>>();
        var matched = new BufferBlock<FlowCorrelationMatch<CorrelationMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Matched, matched);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Timeouts))!.LinkToDiscard();

        await input.Target.SendAsync(new CorrelationMessage("A-100", "response", "done"));
        await input.Target.SendAsync(new CorrelationMessage("A-100", "request", "start"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var match = await matched.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        match.Request.Payload.ShouldBe("start");
        match.Response.Payload.ShouldBe("done");
    }

    [Fact]
    public async Task Correlation_EmitsTimeoutsForUnmatchedInputsOnCompletion()
    {
        var runtimeNode = CreateNode(new { timeoutMilliseconds = 10 });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<CorrelationMessage>>();
        var timeouts = new BufferBlock<FlowCorrelationTimeout<CorrelationMessage>>();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Matched))!.LinkToDiscard();
        LinkOutput(runtimeNode, RoutingComponentPorts.Timeouts, timeouts);

        await input.Target.SendAsync(new CorrelationMessage("A-100", "request", "start"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var timeout = await timeouts.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        timeout.Key.ShouldBe("A-100");
        timeout.Side.ShouldBe("request");
        timeout.Value.Payload.ShouldBe("start");
        timeout.Timeout.ShouldBe(TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public async Task Correlation_ExpiresPendingInputsBeforeProcessingNextInput()
    {
        // Drive expiry through an explicitly-moved wall clock instead of a real-time
        // sleep: ManualTimeProvider's timer never fires, so the first input can only
        // expire via the EmitExpiredAsync at the start of processing the second input
        // -- exactly what this test verifies. Advancing the clock past the timeout is
        // deterministic under load, unlike a fixed Task.Delay.
        var startedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var clock = new ManualTimeProvider(startedAt);
        var firstInputEvaluated = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runtimeNode = CreateNode(
            new { timeoutMilliseconds = 25 },
            (expression, context, resultType) =>
            {
                if (context.Variables["payload"]?.Equals("start") == true)
                {
                    firstInputEvaluated.TrySetResult(null);
                }

                return EvaluateCorrelationExpression(expression, context, resultType);
            },
            clock);
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<CorrelationMessage>>();
        var matched = new BufferBlock<FlowCorrelationMatch<CorrelationMessage>>();
        var timeouts = new BufferBlock<FlowCorrelationTimeout<CorrelationMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Matched, matched);
        LinkOutput(runtimeNode, RoutingComponentPorts.Timeouts, timeouts);

        await input.Target.SendAsync(new CorrelationMessage("A-100", "request", "start"));
        await firstInputEvaluated.Task.WaitAsync(TimeSpan.FromSeconds(30));
        clock.SetUtcNow(startedAt.AddMilliseconds(100));
        await input.Target.SendAsync(new CorrelationMessage("A-100", "response", "done"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var firstTimeout = await timeouts.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var secondTimeout = await timeouts.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        firstTimeout.Side.ShouldBe("request");
        secondTimeout.Side.ShouldBe("response");
        matched.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Correlation_DuplicateSideWarnsAndKeepsOriginalDeadline()
    {
        var startedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var clock = new ManualTimeProvider(startedAt);
        var firstEvaluated = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var duplicateEvaluated = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runtimeNode = CreateNode(
            new { timeoutMilliseconds = 100 },
            (expression, context, resultType) =>
            {
                if (context.Variables["payload"]?.Equals("first") == true)
                {
                    firstEvaluated.TrySetResult(null);
                }

                if (context.Variables["payload"]?.Equals("second") == true)
                {
                    duplicateEvaluated.TrySetResult(null);
                }

                return EvaluateCorrelationExpression(expression, context, resultType);
            },
            clock);
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<CorrelationMessage>>();
        var errors = new BufferBlock<FlowError>();
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        var timeouts = new BufferBlock<FlowCorrelationTimeout<CorrelationMessage>>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);
        LinkOutput(runtimeNode, RoutingComponentPorts.Errors, errors);
        LinkOutput(runtimeNode, RoutingComponentPorts.Timeouts, timeouts);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Matched))!.LinkToDiscard();

        await input.Target.SendAsync(new CorrelationMessage("A-100", "request", "first"));
        await firstEvaluated.Task.WaitAsync(TimeSpan.FromSeconds(30));
        clock.SetUtcNow(startedAt.AddMilliseconds(50));
        await input.Target.SendAsync(new CorrelationMessage("A-100", "request", "second"));
        await duplicateEvaluated.Task.WaitAsync(TimeSpan.FromSeconds(30));
        clock.SetUtcNow(startedAt.AddMilliseconds(120));
        await input.Target.SendAsync(new CorrelationMessage("B-200", "request", "other"));

        var timeout = await timeouts.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        errors.TryReceive(out _).ShouldBeFalse();
        var duplicate = await ReceiveDiagnosticAsync(
            diagnostics,
            RoutingDiagnosticNames.CorrelationDuplicateSide);
        duplicate.Level.ShouldBe(FlowDiagnosticLevel.Warning);
        duplicate.Attributes["key"].ShouldBe("A-100");
        duplicate.Attributes["side"].ShouldBe("request");
        timeout.Key.ShouldBe("A-100");
        timeout.Side.ShouldBe("request");
        timeout.Value.Payload.ShouldBe("second");
        timeout.ReceivedAt.ShouldBe(startedAt);
        timeout.TimedOutAt.ShouldBe(startedAt.AddMilliseconds(120));
    }

    [Fact]
    public async Task Correlation_ReportsExpressionFailureAndContinues()
    {
        var runtimeNode = CreateNode(
            new { expressionName = "pairing" },
            (expression, context, _) =>
            {
                if (context.Variables["payload"]?.Equals("throw") == true)
                {
                    throw new InvalidOperationException("key failed");
                }

                return expression switch
                {
                    "key" => context.Variables["key"],
                    "side" => context.Variables["side"],
                    _ => null
                };
            });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<CorrelationMessage>>();
        var errors = new BufferBlock<FlowError>();
        var matched = new BufferBlock<FlowCorrelationMatch<CorrelationMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Errors, errors);
        LinkOutput(runtimeNode, RoutingComponentPorts.Matched, matched);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Timeouts))!.LinkToDiscard();

        await input.Target.SendAsync(new CorrelationMessage("A-100", "request", "throw"));
        await input.Target.SendAsync(new CorrelationMessage("A-101", "request", "start"));
        await input.Target.SendAsync(new CorrelationMessage("A-101", "response", "done"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(RoutingErrorCodes.CorrelationKeyFailed);
        error.Context!.ShouldContain("expressionName=pairing");
        (await matched.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Key.ShouldBe("A-101");
    }

    [Fact]
    public async Task Correlation_RejectsInvalidSideAndContinues()
    {
        var runtimeNode = CreateNode(new { });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<CorrelationMessage>>();
        var errors = new BufferBlock<FlowError>();
        var matched = new BufferBlock<FlowCorrelationMatch<CorrelationMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Errors, errors);
        LinkOutput(runtimeNode, RoutingComponentPorts.Matched, matched);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Timeouts))!.LinkToDiscard();

        await input.Target.SendAsync(new CorrelationMessage("A-100", "other", "bad"));
        await input.Target.SendAsync(new CorrelationMessage("A-100", "request", "start"));
        await input.Target.SendAsync(new CorrelationMessage("A-100", "response", "done"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(RoutingErrorCodes.CorrelationInvalidSide);
        (await matched.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Key.ShouldBe("A-100");
    }

    [Fact]
    public async Task Correlation_ReportsCapacityLimitAndContinues()
    {
        var runtimeNode = CreateNode(new { maxPending = 1 });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<CorrelationMessage>>();
        var errors = new BufferBlock<FlowError>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Errors, errors);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Matched))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Timeouts))!.LinkToDiscard();

        await input.Target.SendAsync(new CorrelationMessage("A-100", "request", "start"));
        await input.Target.SendAsync(new CorrelationMessage("A-101", "request", "next"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(RoutingErrorCodes.CorrelationCapacityExceeded);
        error.Context!.ShouldContain("key=A-101");
    }

    [Fact]
    public async Task Correlation_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(new { expressionId = "corr-v1" });
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<CorrelationMessage>>();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Matched))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Timeouts))!.LinkToDiscard();

        await input.Target.SendAsync(new CorrelationMessage("A-100", "request", "start"));
        await input.Target.SendAsync(new CorrelationMessage("A-100", "response", "done"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        diagnostic.Name.ShouldBe(RoutingDiagnosticNames.CorrelationMatched);
        diagnostic.Attributes["key"].ShouldBe("A-100");
        diagnostic.Attributes["expressionId"].ShouldBe("corr-v1");
    }

    [Fact]
    public void Correlation_RejectsMissingKeyExpression()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { keyExpression = "" }));

        exception.Message.ShouldContain("keyExpression");
    }

    [Fact]
    public void Correlation_RejectsEqualSides()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                requestSide = "message",
                responseSide = "message"
            }));

        exception.Message.ShouldContain("different");
    }

    private static RuntimeNode CreateNode(
        object overrides,
        Func<string, FlowMapContext, Type, object?>? evaluate = null,
        TimeProvider? clock = null)
    {
        var configuration = RoutingTestHost.MergeConfiguration(
            new
            {
                keyExpression = "key",
                sideExpression = "side",
                inputType = "app.correlation"
            },
            overrides);
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterRoutingComponents(options =>
            {
                options
                    .UseExpressionEngine(new RecordingExpressionEngine(
                        evaluate: evaluate ?? EvaluateCorrelationExpression))
                    .RegisterType<CorrelationMessage>("app.correlation")
                    .UseContextFactory(new CorrelationMessageContextFactory());
                if (clock is not null)
                {
                    options.UseClock(clock);
                }
            });
        registry.TryGetFactory(RoutingComponentTypes.Correlation, out var factory).ShouldBeTrue();
        return factory(RoutingTestHost.CreateContext(RoutingComponentTypes.Correlation, configuration));
    }

    private static RuntimeNode CreateSplitNode(
        object overrides,
        Func<string, FlowMapContext, Type, object?>? evaluate = null)
    {
        var configuration = RoutingTestHost.MergeConfiguration(
            new
            {
                keyExpression = "key",
                inputType = "app.correlation"
            },
            overrides);
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterRoutingComponents(options => options
                .UseExpressionEngine(new RecordingExpressionEngine(
                    evaluate: evaluate ?? EvaluateCorrelationExpression))
                .RegisterType<CorrelationMessage>("app.correlation")
                .UseContextFactory(new CorrelationMessageContextFactory()));
        registry.TryGetFactory(RoutingComponentTypes.Correlation, out var factory).ShouldBeTrue();
        return factory(RoutingTestHost.CreateContext(RoutingComponentTypes.Correlation, configuration));
    }

    private static object? EvaluateCorrelationExpression(
        string expression,
        FlowMapContext context,
        Type resultType)
    {
        return expression switch
        {
            "key" => context.Variables["key"],
            "side" => context.Variables["side"],
            _ => context.Variables["input"]
        };
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

    private static async Task<FlowDiagnostic> ReceiveDiagnosticAsync(
        BufferBlock<FlowDiagnostic> diagnostics,
        string name)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (await diagnostics.OutputAvailableAsync(cancellation.Token))
        {
            var diagnostic = await diagnostics.ReceiveAsync(cancellation.Token);
            if (diagnostic.Name == name)
            {
                return diagnostic;
            }
        }

        throw new InvalidOperationException($"diagnostic '{name}' was not emitted.");
    }

    private sealed record CorrelationMessage(string Key, string Side, string Payload);

    // Replaces the old ManualRoutingClock: the wall clock is moved explicitly via
    // SetUtcNow while the scheduled timer never fires. FakeTimeProvider cannot
    // express this (its SetUtcNow/Advance fire due timers), so this test keeps a
    // bespoke provider whose timers are inert and drives expiry through the
    // input-time path exactly as the original test did.
    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly object _gate = new();
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }

        public void SetUtcNow(DateTimeOffset utcNow)
        {
            lock (_gate)
            {
                _utcNow = utcNow;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
            => new NeverFiringTimer();

        private sealed class NeverFiringTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class CorrelationMessageContextFactory : IFlowMapContextFactory<CorrelationMessage>
    {
        public FlowMapContext Create(CorrelationMessage input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["key"] = input.Key,
                    ["side"] = input.Side,
                    ["payload"] = input.Payload
                }
            };
    }
}
