using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Engine.Tests;

/// <summary>
/// Regression tests for the error fanout channel, per-link failure isolation,
/// upstream fault propagation, and the stricter definition validation rules.
/// </summary>
public sealed class RuntimeErrorChannelTests
{
    [Fact]
    public async Task NodeErrors_FanOutToEveryLinkedConsumer()
    {
        var node = new ThrowingSinkNode();
        var first = new BufferBlock<FlowError>();
        var second = new BufferBlock<FlowError>();
        node.Errors.LinkTo(first, new DataflowLinkOptions { PropagateCompletion = true });
        node.Errors.LinkTo(second, new DataflowLinkOptions { PropagateCompletion = true });

        await node.Input.SendAsync(1);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var firstErrors = await ReceiveAllAsync(first).WaitAsync(TimeSpan.FromSeconds(5));
        var secondErrors = await ReceiveAllAsync(second).WaitAsync(TimeSpan.FromSeconds(5));

        firstErrors.ShouldHaveSingleItem().Code.ShouldBe(FlowErrorCodes.ProcessingFailed);
        secondErrors.ShouldHaveSingleItem().Code.ShouldBe(FlowErrorCodes.ProcessingFailed);
    }

    [Fact]
    public async Task NodeErrors_KeepFlowingWhenNoConsumerIsLinked()
    {
        // Unobserved errors must be discarded, not buffered without bound.
        var node = new ThrowingSinkNode();

        for (var value = 1; value <= 100; value++)
        {
            await node.Input.SendAsync(value).WaitAsync(TimeSpan.FromSeconds(5));
        }

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        node.HandledCount.ShouldBe(100);
    }

    [Fact]
    public async Task OutputPort_DetachesRejectingLinkAndKeepsDeliveringToSiblings()
    {
        var source = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 1 });
        var output = new OutputPort<int>(
            new PortAddress("main", new NodeName("source"), new PortName("Output")),
            source);
        var healthy = new BufferBlock<int>();
        var dying = new BufferBlock<int>();
        var failures = new List<OutputPortLinkFailure>();
        output.LinkFailed += failures.Add;

        output.TryLinkTo(Input("healthy", healthy), propagateCompletion: true, out _).ShouldNotBeNull();
        output.TryLinkTo(Input("dying", dying), propagateCompletion: false, out _).ShouldNotBeNull();

        await source.SendAsync(1);
        await WaitForAsync(() => healthy.Count == 1);

        // The dying target stops accepting messages mid-flow.
        dying.Complete();

        for (var value = 2; value <= 5; value++)
        {
            await source.SendAsync(value);
        }

        source.Complete();
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var received = await ReceiveAllAsync(healthy).WaitAsync(TimeSpan.FromSeconds(5));
        received.ShouldBe([1, 2, 3, 4, 5]);
        failures.ShouldContain(f => f.Reason == OutputPortLinkFailureReason.TargetRejected);
    }

    [Fact]
    public async Task OutputPort_ConditionExceptionDropsMessageForThatLinkOnly()
    {
        var source = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 1 });
        var output = new OutputPort<int>(
            new PortAddress("main", new NodeName("source"), new PortName("Output")),
            source);
        var conditional = new BufferBlock<int>();
        var unconditional = new BufferBlock<int>();
        var failures = new List<OutputPortLinkFailure>();
        output.LinkFailed += failures.Add;

        output.TryLinkTo(
            Input("conditional", conditional),
            propagateCompletion: true,
            new DelegateFlowPredicate<object?>(value =>
                (int)value! == 3 ? throw new InvalidOperationException("boom") : true),
            out _).ShouldNotBeNull();
        output.TryLinkTo(Input("unconditional", unconditional), propagateCompletion: true, out _)
            .ShouldNotBeNull();

        for (var value = 1; value <= 5; value++)
        {
            await source.SendAsync(value);
        }

        source.Complete();
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await ReceiveAllAsync(conditional).WaitAsync(TimeSpan.FromSeconds(5)))
            .ShouldBe([1, 2, 4, 5]);
        (await ReceiveAllAsync(unconditional).WaitAsync(TimeSpan.FromSeconds(5)))
            .ShouldBe([1, 2, 3, 4, 5]);
        failures.ShouldHaveSingleItem().Reason.ShouldBe(OutputPortLinkFailureReason.ConditionFailed);
    }

    [Fact]
    public async Task OutputPort_PropagatesSourceFaultToLinkedTargets()
    {
        var source = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 1 });
        var output = new OutputPort<int>(
            new PortAddress("main", new NodeName("source"), new PortName("Output")),
            source);
        var target = new BufferBlock<int>();
        output.TryLinkTo(Input("target", target), propagateCompletion: true, out _).ShouldNotBeNull();

        await source.SendAsync(1);
        ((IDataflowBlock)source).Fault(new InvalidOperationException("upstream failed"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => output.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        await Should.ThrowAsync<InvalidOperationException>(
            () => target.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Runtime_SurfacesNodeErrorsThroughErrorChannel()
    {
        var registry = new RuntimeNodeFactoryRegistry();
        registry.Register(new NodeType("test.failing-sink"), ctx =>
        {
            var node = new ThrowingSinkNode();
            return RuntimeNode.Create(
                ctx.Address,
                node,
                inputs: [new InputPort<int>(ctx.Address.Port(new PortName("Input")), node.Input)]);
        });
        registry.Register(new NodeType("test.int-source"), ctx =>
        {
            var node = new IntSourceNode();
            return RuntimeNode.Create(
                ctx.Address,
                node,
                outputs: [new OutputPort<int>(ctx.Address.Port(new PortName("Output")), node.Output)]);
        });

        var definition = new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["main"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["source"] = new() { Type = new NodeType("test.int-source") },
                        ["sink"] = new()
                        {
                            Type = new NodeType("test.failing-sink"),
                            Ports = { ["Input"] = JsonValue("source.Output") }
                        }
                    }
                }
            }
        };

        var result = new ApplicationRuntimeBuilder(registry).Build(definition);
        result.IsSuccess.ShouldBeTrue();

        await using var runtime = result.Runtime!;
        var errors = new BufferBlock<RuntimeFlowError>();
        runtime.Errors.LinkTo(errors, new DataflowLinkOptions { PropagateCompletion = true });

        await runtime.StartAsync();
        var sourceNode = (IntSourceNode)runtime.Workflows.Single().Nodes
            .Single(node => node.Address.Node.Value == "source").Node;
        await sourceNode.Output.SendAsync(7);
        runtime.Complete();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var received = await ReceiveAllAsync(errors).WaitAsync(TimeSpan.FromSeconds(5));
        var error = received.ShouldHaveSingleItem();
        error.Code.ShouldBe(FlowErrorCodes.ProcessingFailed);
        error.NodeAddress.Node.Value.ShouldBe("sink");
    }

    [Fact]
    public async Task Runtime_ReportsThrowingLinkConditionAndKeepsFlowing()
    {
        var registry = new RuntimeNodeFactoryRegistry();
        registry.Register(new NodeType("test.int-source"), ctx =>
        {
            var node = new IntSourceNode();
            return RuntimeNode.Create(
                ctx.Address,
                node,
                outputs: [new OutputPort<int>(ctx.Address.Port(new PortName("Output")), node.Output)]);
        });
        registry.Register(new NodeType("test.collecting-sink"), ctx =>
        {
            var node = new CollectingSinkNode();
            return RuntimeNode.Create(
                ctx.Address,
                node,
                inputs: [new InputPort<int>(ctx.Address.Port(new PortName("Input")), node.Input)]);
        });

        var definition = new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["main"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["source"] = new() { Type = new NodeType("test.int-source") },
                        ["sink"] = new()
                        {
                            Type = new NodeType("test.collecting-sink"),
                            Ports =
                            {
                                ["Input"] = JsonValue(new { from = "source.Output", when = "input != 2" })
                            }
                        }
                    }
                }
            }
        };

        var result = new ApplicationRuntimeBuilder(
            registry,
            linkConditionExpressionEngine: new ThrowOnTwoExpressionEngine()).Build(definition);
        result.IsSuccess.ShouldBeTrue();

        await using var runtime = result.Runtime!;
        var errors = new BufferBlock<RuntimeFlowError>();
        runtime.Errors.LinkTo(errors, new DataflowLinkOptions { PropagateCompletion = true });

        await runtime.StartAsync();
        var sourceNode = (IntSourceNode)runtime.Workflows.Single().Nodes
            .Single(node => node.Address.Node.Value == "source").Node;
        var sinkNode = (CollectingSinkNode)runtime.Workflows.Single().Nodes
            .Single(node => node.Address.Node.Value == "sink").Node;

        for (var value = 1; value <= 3; value++)
        {
            await sourceNode.Output.SendAsync(value);
        }

        runtime.Complete();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        sinkNode.Received.ShouldBe([1, 3]);
        var received = await ReceiveAllAsync(errors).WaitAsync(TimeSpan.FromSeconds(5));
        received.ShouldContain(error => error.Code == FlowErrorCodes.DynamicExpressionFailed);
    }

    [Fact]
    public void Validator_RejectsCyclicLinks()
    {
        var definition = new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["main"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["a"] = new()
                        {
                            Type = new NodeType("test.node"),
                            Ports = { ["Input"] = JsonValue("b.Output") }
                        },
                        ["b"] = new()
                        {
                            Type = new NodeType("test.node"),
                            Ports = { ["Input"] = JsonValue("a.Output") }
                        }
                    }
                }
            }
        };

        var result = new ApplicationDefinitionValidator().Validate(definition);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error =>
            error.Code == ApplicationDefinitionValidationErrorCode.CyclicLink);
    }

    [Fact]
    public void Validator_RejectsNamesThatViolateRuntimeInvariants()
    {
        var definition = new ApplicationDefinition
        {
            Resources = new Dictionary<string, NodeDefinition>
            {
                ["res.broker"] = new() { Type = new NodeType("test.node") }
            },
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["my.flow"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["a.b"] = new() { Type = new NodeType("test.node") }
                    }
                },
                ["$resources"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["ok"] = new() { Type = new NodeType("test.node") }
                    }
                }
            }
        };

        var result = new ApplicationDefinitionValidator().Validate(definition);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error =>
            error.Code == ApplicationDefinitionValidationErrorCode.InvalidResourceName);
        result.Errors.ShouldContain(error =>
            error.Code == ApplicationDefinitionValidationErrorCode.InvalidWorkflowName &&
            error.Message.Contains("my.flow"));
        result.Errors.ShouldContain(error =>
            error.Code == ApplicationDefinitionValidationErrorCode.InvalidWorkflowName &&
            error.Message.Contains("reserved"));
        result.Errors.ShouldContain(error =>
            error.Code == ApplicationDefinitionValidationErrorCode.InvalidNodeName);
    }

    [Fact]
    public async Task Runtime_RejectsSecondStart()
    {
        var registry = new RuntimeNodeFactoryRegistry();
        registry.Register(new NodeType("test.int-source"), ctx =>
        {
            var node = new IntSourceNode();
            return RuntimeNode.Create(
                ctx.Address,
                node,
                outputs: [new OutputPort<int>(ctx.Address.Port(new PortName("Output")), node.Output)]);
        });

        var definition = new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["main"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["source"] = new() { Type = new NodeType("test.int-source") }
                    }
                }
            }
        };

        var result = new ApplicationRuntimeBuilder(registry).Build(definition);
        result.IsSuccess.ShouldBeTrue();

        await using var runtime = result.Runtime!;
        await runtime.StartAsync();

        await Should.ThrowAsync<InvalidOperationException>(() => runtime.StartAsync());

        runtime.Complete();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static InputPort<int> Input(string name, ITargetBlock<int> target)
        => new(new PortAddress("main", new NodeName(name), new PortName("Input")), target);

    private static JsonElement JsonValue<T>(T value)
        => JsonSerializer.SerializeToElement(value);

    private static async Task<IReadOnlyList<T>> ReceiveAllAsync<T>(BufferBlock<T> block)
    {
        var received = new List<T>();
        while (await block.OutputAvailableAsync().ConfigureAwait(false))
        {
            while (block.TryReceive(out var item))
            {
                received.Add(item);
            }
        }

        return received;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        condition().ShouldBeTrue();
    }

    private sealed class ThrowingSinkNode : SinkFlowNode<int>
    {
        private int _handled;

        public int HandledCount => Volatile.Read(ref _handled);

        protected override ValueTask HandleAsync(int input, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _handled);
            throw new InvalidOperationException($"Input {input} always fails.");
        }
    }

    private sealed class CollectingSinkNode : SinkFlowNode<int>
    {
        private readonly List<int> _received = [];

        public IReadOnlyList<int> Received
        {
            get
            {
                lock (_received)
                {
                    return [.. _received];
                }
            }
        }

        protected override ValueTask HandleAsync(int input, CancellationToken cancellationToken)
        {
            lock (_received)
            {
                _received.Add(input);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class IntSourceNode : FlowNodeBase
    {
        public IntSourceNode()
        {
            CompleteWhen(Output.Completion);
        }

        public BufferBlock<int> Output { get; } = new();

        public override void Complete() => Output.Complete();
    }

    private sealed class ThrowOnTwoExpressionEngine : IFlowExpressionEngine
    {
        public string Name => "test.throw-on-two";

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
        {
            var value = (int)context.Variables["input"]!;
            return value == 2
                ? throw new InvalidOperationException("Expression failed for value 2.")
                : true;
        }
    }
}
