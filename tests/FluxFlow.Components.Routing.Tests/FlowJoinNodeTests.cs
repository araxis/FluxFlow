using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class FlowJoinNodeTests
{
    [Fact]
    public async Task Join_MatchesLeftAndRightByKey()
    {
        var runtimeNode = CreateNode(new { });
        var left = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Left))
            .ShouldBeOfType<InputPort<LeftMessage>>();
        var right = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Right))
            .ShouldBeOfType<InputPort<RightMessage>>();
        var output = new BufferBlock<FlowJoinResult<LeftMessage, RightMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Output, output);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Timeouts))!.LinkToDiscard();

        await left.Target.SendAsync(new LeftMessage("A-100", "left"));
        await right.Target.SendAsync(new RightMessage("A-100", "right"));
        left.Target.Complete();
        right.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Key.ShouldBe("A-100");
        result.Left.Payload.ShouldBe("left");
        result.Right.Payload.ShouldBe("right");
        result.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task Join_MatchesOutOfOrderRightAndLeft()
    {
        var runtimeNode = CreateNode(new { });
        var left = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Left))
            .ShouldBeOfType<InputPort<LeftMessage>>();
        var right = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Right))
            .ShouldBeOfType<InputPort<RightMessage>>();
        var output = new BufferBlock<FlowJoinResult<LeftMessage, RightMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Output, output);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Timeouts))!.LinkToDiscard();

        await right.Target.SendAsync(new RightMessage("A-100", "right"));
        await left.Target.SendAsync(new LeftMessage("A-100", "left"));
        left.Target.Complete();
        right.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Left.Payload.ShouldBe("left");
        result.Right.Payload.ShouldBe("right");
    }

    [Fact]
    public async Task Join_PairsDuplicateKeysInOrder()
    {
        var runtimeNode = CreateNode(new { });
        var left = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Left))
            .ShouldBeOfType<InputPort<LeftMessage>>();
        var right = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Right))
            .ShouldBeOfType<InputPort<RightMessage>>();
        var output = new BufferBlock<FlowJoinResult<LeftMessage, RightMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Output, output);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Timeouts))!.LinkToDiscard();

        await left.Target.SendAsync(new LeftMessage("A-100", "left-1"));
        await left.Target.SendAsync(new LeftMessage("A-100", "left-2"));
        await right.Target.SendAsync(new RightMessage("A-100", "right-1"));
        await right.Target.SendAsync(new RightMessage("A-100", "right-2"));
        left.Target.Complete();
        right.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        first.Left.Payload.ShouldBe("left-1");
        first.Right.Payload.ShouldBe("right-1");
        second.Left.Payload.ShouldBe("left-2");
        second.Right.Payload.ShouldBe("right-2");
    }

    [Fact]
    public async Task Join_EmitsTimeoutWhenTimerExpires()
    {
        var runtimeNode = CreateNode(new { timeoutMilliseconds = 25 });
        var left = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Left))
            .ShouldBeOfType<InputPort<LeftMessage>>();
        var right = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Right))
            .ShouldBeOfType<InputPort<RightMessage>>();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Output))!.LinkToDiscard();
        var timeouts = new BufferBlock<FlowJoinTimeout<LeftMessage, RightMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Timeouts, timeouts);

        await left.Target.SendAsync(new LeftMessage("A-100", "left"));
        var timeout = await timeouts.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        left.Target.Complete();
        right.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        timeout.Key.ShouldBe("A-100");
        timeout.Side.ShouldBe(FlowJoinSide.Left);
        timeout.Left!.Payload.ShouldBe("left");
        timeout.Right.ShouldBeNull();
        timeout.Timeout.ShouldBe(TimeSpan.FromMilliseconds(25));
    }

    [Fact]
    public async Task Join_EmitsTimeoutsForRemainingInputsOnCompletion()
    {
        var runtimeNode = CreateNode(new { timeoutMilliseconds = 5_000 });
        var left = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Left))
            .ShouldBeOfType<InputPort<LeftMessage>>();
        var right = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Right))
            .ShouldBeOfType<InputPort<RightMessage>>();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Output))!.LinkToDiscard();
        var timeouts = new BufferBlock<FlowJoinTimeout<LeftMessage, RightMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Timeouts, timeouts);

        await right.Target.SendAsync(new RightMessage("A-100", "right"));
        left.Target.Complete();
        right.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var timeout = await timeouts.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        timeout.Key.ShouldBe("A-100");
        timeout.Side.ShouldBe(FlowJoinSide.Right);
        timeout.Right!.Payload.ShouldBe("right");
    }

    [Fact]
    public async Task Join_ReportsExpressionFailureAndContinues()
    {
        var runtimeNode = CreateNode(
            new { expressionName = "join-v1" },
            (expression, context, _) =>
            {
                if (context.Variables["payload"]?.Equals("throw") == true)
                {
                    throw new InvalidOperationException("key failed");
                }

                return context.Variables["key"];
            });
        var left = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Left))
            .ShouldBeOfType<InputPort<LeftMessage>>();
        var right = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Right))
            .ShouldBeOfType<InputPort<RightMessage>>();
        var errors = new BufferBlock<FlowError>();
        var output = new BufferBlock<FlowJoinResult<LeftMessage, RightMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Errors, errors);
        LinkOutput(runtimeNode, RoutingComponentPorts.Output, output);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Timeouts))!.LinkToDiscard();

        await left.Target.SendAsync(new LeftMessage("A-100", "throw"));
        await left.Target.SendAsync(new LeftMessage("A-101", "left"));
        await right.Target.SendAsync(new RightMessage("A-101", "right"));
        left.Target.Complete();
        right.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(RoutingErrorCodes.JoinLeftKeyFailed);
        error.Context!.ShouldContain("expressionName=join-v1");
        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Key.ShouldBe("A-101");
    }

    [Fact]
    public async Task Join_ReportsProcessingFailureAndContinues()
    {
        var clock = new ThrowingTimeProvider();
        var leftNodeContext = new RoutingNodeContext
        {
            Address = new NodeAddress("main", new NodeName("join")),
            NodeType = RoutingComponentTypes.Join,
            InputType = typeof(LeftMessage)
        };
        var engine = new RecordingExpressionEngine(evaluate: (_, context, _) => context.Variables["key"]);
        var leftFactory = new MessageContextFactory();
        var rightFactory = new MessageContextFactory();
        var rightNodeContext = leftNodeContext with { InputType = typeof(RightMessage) };
        var node = new FlowJoinNode<LeftMessage, RightMessage>(
            new JoinRoutingOptions
            {
                LeftKeyExpression = "key",
                RightKeyExpression = "key"
            },
            left => SelectKey(engine, "key", leftFactory, leftNodeContext, left),
            right => SelectKey(engine, "key", rightFactory, rightNodeContext, right),
            clock,
            engine.Name);
        var errors = new BufferBlock<FlowError>();
        var output = new BufferBlock<FlowJoinResult<LeftMessage, RightMessage>>();
        node.Errors.LinkTo(errors, new DataflowLinkOptions { PropagateCompletion = true });
        node.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });
        node.Timeouts.LinkTo(DataflowBlock.NullTarget<FlowJoinTimeout<LeftMessage, RightMessage>>());

        await node.Left.SendAsync(new LeftMessage("A-100", "boom"));
        await node.Left.SendAsync(new LeftMessage("A-101", "left"));
        await node.Right.SendAsync(new RightMessage("A-101", "right"));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(RoutingErrorCodes.JoinFailed);
        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Key.ShouldBe("A-101");
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Join_ReportsCapacityLimitAndContinues()
    {
        var runtimeNode = CreateNode(new { maxPending = 1 });
        var left = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Left))
            .ShouldBeOfType<InputPort<LeftMessage>>();
        var right = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Right))
            .ShouldBeOfType<InputPort<RightMessage>>();
        var errors = new BufferBlock<FlowError>();
        var output = new BufferBlock<FlowJoinResult<LeftMessage, RightMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Errors, errors);
        LinkOutput(runtimeNode, RoutingComponentPorts.Output, output);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Timeouts))!.LinkToDiscard();
        var errorTask = errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var outputTask = output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        await left.Target.SendAsync(new LeftMessage("A-100", "left-1"));
        await left.Target.SendAsync(new LeftMessage("A-101", "left-2"));

        var error = await errorTask;
        error.Code.ShouldBe(RoutingErrorCodes.JoinCapacityExceeded);
        error.Context!.ShouldContain("key=A-101");

        await right.Target.SendAsync(new RightMessage("A-100", "right-1"));
        left.Target.Complete();
        right.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await outputTask).Key.ShouldBe("A-100");
    }

    [Fact]
    public async Task Join_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(new { expressionId = "join-v1" });
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);
        var left = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Left))
            .ShouldBeOfType<InputPort<LeftMessage>>();
        var right = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Right))
            .ShouldBeOfType<InputPort<RightMessage>>();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Output))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Timeouts))!.LinkToDiscard();

        await left.Target.SendAsync(new LeftMessage("A-100", "left"));
        await right.Target.SendAsync(new RightMessage("A-100", "right"));
        left.Target.Complete();
        right.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(RoutingDiagnosticNames.JoinMatched);
        diagnostic.Attributes["key"].ShouldBe("A-100");
        diagnostic.Attributes["expressionId"].ShouldBe("join-v1");
    }

    [Fact]
    public async Task Join_DisposeAfterFaultDoesNotThrow()
    {
        var leftNodeContext = new RoutingNodeContext
        {
            Address = new NodeAddress("main", new NodeName("join")),
            NodeType = RoutingComponentTypes.Join,
            InputType = typeof(LeftMessage)
        };
        var engine = new RecordingExpressionEngine();
        var leftFactory = new TestRoutingContextFactory();
        var rightFactory = new TestRoutingContextFactory();
        var rightNodeContext = leftNodeContext with { InputType = typeof(RightMessage) };
        var node = new FlowJoinNode<LeftMessage, RightMessage>(
            new JoinRoutingOptions
            {
                LeftKeyExpression = "key",
                RightKeyExpression = "key"
            },
            left => SelectKey(engine, "key", leftFactory, leftNodeContext, left),
            right => SelectKey(engine, "key", rightFactory, rightNodeContext, right),
            engine.Name);

        node.Fault(new InvalidOperationException("boom"));
        await node.DisposeAsync();

        node.Completion.IsFaulted.ShouldBeTrue();
    }

    [Fact]
    public void Join_RejectsMissingLeftKeyExpression()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { leftKeyExpression = "" }));

        exception.Message.ShouldContain("leftKeyExpression");
    }

    [Fact]
    public void Join_RejectsInvalidCapacity()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { boundedCapacity = 0 }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void Join_RejectsUnknownRightInputType()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { rightInputType = "missing.type" }));

        exception.Message.ShouldContain("not registered");
    }

    private static RuntimeNode CreateNode(
        object overrides,
        Func<string, FlowMapContext, Type, object?>? evaluate = null)
    {
        var configuration = RoutingTestHost.MergeConfiguration(
            new
            {
                leftKeyExpression = "leftKey",
                rightKeyExpression = "rightKey",
                leftInputType = "app.left",
                rightInputType = "app.right"
            },
            overrides);
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterRoutingComponents(options => options
                .UseExpressionEngine(new RecordingExpressionEngine(
                    evaluate: evaluate ?? EvaluateJoinExpression))
                .RegisterType<LeftMessage>("app.left")
                .RegisterType<RightMessage>("app.right")
                .UseContextFactory(new LeftMessageContextFactory())
                .UseContextFactory(new RightMessageContextFactory()));
        registry.TryGetFactory(RoutingComponentTypes.Join, out var factory).ShouldBeTrue();
        return factory(RoutingTestHost.CreateContext(RoutingComponentTypes.Join, configuration));
    }

    private static object? EvaluateJoinExpression(
        string expression,
        FlowMapContext context,
        Type resultType)
        => context.Variables["key"];

    // Reproduces the selector the factory compiles once at build time: evaluate
    // the expression through the engine (RecordingExpressionEngine's default
    // Compile defers to Evaluate, preserving interception) over the context the
    // factory creates for the input, then normalize the result to a string.
    private static string? SelectKey<TInput>(
        IFlowExpressionEngine engine,
        string expression,
        IRoutingContextFactory factory,
        RoutingNodeContext nodeContext,
        TInput input)
    {
        var value = engine.Compile<object?>(expression).Evaluate(factory.Create(input, nodeContext));
        return value switch
        {
            null => null,
            string text => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            _ => string.IsNullOrWhiteSpace(value.ToString()) ? null : value.ToString()!.Trim()
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

    private sealed record LeftMessage(string Key, string Payload);

    private sealed record RightMessage(string Key, string Payload);

    private sealed class TestRoutingContextFactory : IRoutingContextFactory
    {
        public FlowMapContext Create(object? input, RoutingNodeContext context)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input
                }
            };
    }

    private sealed class MessageContextFactory : IRoutingContextFactory
    {
        public FlowMapContext Create(object? input, RoutingNodeContext context)
        {
            var (key, payload) = input switch
            {
                LeftMessage left => (left.Key, left.Payload),
                RightMessage right => (right.Key, right.Payload),
                _ => (null, null)
            };

            return new FlowMapContext
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["key"] = key,
                    ["payload"] = payload
                }
            };
        }
    }

    private sealed class LeftMessageContextFactory : IFlowMapContextFactory<LeftMessage>
    {
        public FlowMapContext Create(LeftMessage input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["key"] = input.Key,
                    ["payload"] = input.Payload
                }
            };
    }

    private sealed class RightMessageContextFactory : IFlowMapContextFactory<RightMessage>
    {
        public FlowMapContext Create(RightMessage input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["key"] = input.Key,
                    ["payload"] = input.Payload
                }
            };
    }
}
