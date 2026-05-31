using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class NodeAuthoringTests
{
    [Fact]
    public async Task BaseNodeHelpers_BuildAndRunPipeline()
    {
        var values = new List<int>();
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(new NodeType("test.sequence"), SequenceNode.Create)
            .Register(new NodeType("test.double"), DoubleNode.Create)
            .Register(new NodeType("test.collect"), context => CollectNode.Create(context, values));

        var runtime = BuildRuntime(registry, new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["main"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["source"] = new() { Type = new NodeType("test.sequence") },
                        ["double"] = new()
                        {
                            Type = new NodeType("test.double"),
                            Ports =
                            {
                                ["Input"] = JsonValue("source.Output")
                            }
                        },
                        ["collect"] = new()
                        {
                            Type = new NodeType("test.collect"),
                            Ports =
                            {
                                ["Input"] = JsonValue("double.Output")
                            }
                        }
                    }
                }
            }
        });

        await using var _ = runtime;
        await runtime.StartAsync();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        values.ShouldBe([2, 4, 6]);
    }

    [Fact]
    public async Task RuntimeBuilder_AppliesConditionalLinkExpressions()
    {
        var evenValues = new List<int>();
        var oddValues = new List<int>();
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(new NodeType("test.sequence"), SequenceNode.Create)
            .Register(new NodeType("test.collect-even"), context => CollectNode.Create(context, evenValues))
            .Register(new NodeType("test.collect-odd"), context => CollectNode.Create(context, oddValues));

        var runtime = BuildRuntime(registry, new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["main"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["source"] = new() { Type = new NodeType("test.sequence") },
                        ["even"] = new()
                        {
                            Type = new NodeType("test.collect-even"),
                            Ports =
                            {
                                ["Input"] = JsonValue(new
                                {
                                    from = "source.Output",
                                    when = "input % 2 == 0"
                                })
                            }
                        },
                        ["odd"] = new()
                        {
                            Type = new NodeType("test.collect-odd"),
                            Ports =
                            {
                                ["Input"] = JsonValue(new
                                {
                                    from = "source.Output",
                                    when = "input % 2 != 0"
                                })
                            }
                        }
                    }
                }
            }
        });

        await using var _ = runtime;
        await runtime.StartAsync();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        evenValues.ShouldBe([2]);
        oddValues.ShouldBe([1, 3]);
    }

    [Fact]
    public async Task SinkBase_ReportsProcessingErrorsAndCompletes()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(new NodeType("test.sequence"), SequenceNode.Create)
            .Register(new NodeType("test.fail"), FailingSinkNode.Create);

        var runtime = BuildRuntime(registry, new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["main"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["source"] = new() { Type = new NodeType("test.sequence") },
                        ["fail"] = new()
                        {
                            Type = new NodeType("test.fail"),
                            Ports =
                            {
                                ["Input"] = JsonValue("source.Output")
                            }
                        }
                    }
                }
            }
        });

        var sink = runtime.Workflows.Single().Nodes.Single(node => node.Address.Node.Value == "fail");
        var errors = new BufferBlock<FlowError>();
        sink.Node.Errors.LinkTo(errors);

        await using var _ = runtime;
        await runtime.StartAsync();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(FlowErrorCodes.ProcessingFailed);
        error.Message.ShouldContain("failed to process input");
    }

    [Fact]
    public void RegistrationContract_AddsFactoryToRegistry()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(new SequenceNodeRegistration());

        registry.TryGetFactory(new NodeType("test.sequence"), out var factory).ShouldBeTrue();
        factory.ShouldNotBeNull();
    }

    [Fact]
    public void DelegateRegistration_AddsFactoryToRegistry()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(new FlowNodeRegistration(new NodeType("test.sequence"), SequenceNode.Create));

        registry.TryGetFactory(new NodeType("test.sequence"), out var factory).ShouldBeTrue();
        factory.ShouldNotBeNull();
    }

    [Fact]
    public void NodeModule_AddsAllFactoriesToRegistry()
    {
        var module = new FlowNodeModule(
            new FlowNodeRegistration(new NodeType("test.sequence"), SequenceNode.Create),
            new FlowNodeRegistration(new NodeType("test.public-sequence"), PublicSequenceNode.Create));

        var registry = new RuntimeNodeFactoryRegistry()
            .Register(module);

        registry.TryGetFactory(new NodeType("test.sequence"), out _).ShouldBeTrue();
        registry.TryGetFactory(new NodeType("test.public-sequence"), out _).ShouldBeTrue();
    }

    [Fact]
    public void NodeModules_AddAllFactoriesToRegistry()
    {
        var modules = new IFlowNodeModule[]
        {
            new FlowNodeModule(new FlowNodeRegistration(new NodeType("test.sequence"), SequenceNode.Create)),
            new FlowNodeModule(new FlowNodeRegistration(new NodeType("test.fail"), FailingSinkNode.Create))
        };

        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterModules(modules);

        registry.TryGetFactory(new NodeType("test.sequence"), out _).ShouldBeTrue();
        registry.TryGetFactory(new NodeType("test.fail"), out _).ShouldBeTrue();
    }

    [Fact]
    public void RegistrationRange_DoesNotPartiallyRegisterDuplicateSet()
    {
        var registry = new RuntimeNodeFactoryRegistry();
        var duplicate = new NodeType("test.sequence");
        var next = new NodeType("test.public-sequence");

        var exception = Should.Throw<InvalidOperationException>(() => registry.RegisterRange(
        [
            new FlowNodeRegistration(duplicate, SequenceNode.Create),
            new FlowNodeRegistration(duplicate, SequenceNode.Create),
            new FlowNodeRegistration(next, PublicSequenceNode.Create)
        ]));

        exception.Message.ShouldContain(duplicate.Value);
        registry.TryGetFactory(duplicate, out _).ShouldBeFalse();
        registry.TryGetFactory(next, out _).ShouldBeFalse();
    }

    [Fact]
    public void RegistrationRange_DoesNotPartiallyRegisterWhenTypeAlreadyExists()
    {
        var existing = new NodeType("test.sequence");
        var next = new NodeType("test.public-sequence");
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(existing, SequenceNode.Create);

        var exception = Should.Throw<InvalidOperationException>(() => registry.RegisterRange(
        [
            new FlowNodeRegistration(existing, SequenceNode.Create),
            new FlowNodeRegistration(next, PublicSequenceNode.Create)
        ]));

        exception.Message.ShouldContain(existing.Value);
        registry.TryGetFactory(existing, out _).ShouldBeTrue();
        registry.TryGetFactory(next, out _).ShouldBeFalse();
    }

    [Fact]
    public async Task FlowNodeBase_FaultCompletesErrorsAfterQueuedDiagnostics()
    {
        var node = new ErrorReportingNode();
        var errors = new BufferBlock<FlowError>();
        node.Errors.LinkTo(errors, new DataflowLinkOptions { PropagateCompletion = true });

        node.Report();
        node.Fault(new InvalidOperationException("Boom."));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(123);
        error.Message.ShouldBe("Before fault.");
        var faultError = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        faultError.Code.ShouldBe(124);
        faultError.Message.ShouldBe("Fault hook.");
        await errors.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FlowNodeBase_FaultHookCanReportFinalDiagnostic()
    {
        var node = new ErrorReportingNode();
        var errors = new BufferBlock<FlowError>();
        node.Errors.LinkTo(errors, new DataflowLinkOptions { PropagateCompletion = true });

        node.Fault(new InvalidOperationException("Boom."));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(124);
        error.Message.ShouldBe("Fault hook.");
        await errors.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void DataflowBaseNodes_RunFaultHookInsideFaultCall()
    {
        AssertFaultHookRuns(new FaultHookSourceNode());
        AssertFaultHookRuns(new FaultHookSinkNode());
        AssertFaultHookRuns(new FaultHookMapNode());

        static void AssertFaultHookRuns(IFlowNode node)
        {
            var exception = Should.Throw<InvalidOperationException>(
                () => node.Fault(new InvalidOperationException("Boom.")));

            exception.Message.ShouldBe("Fault hook failed.");
        }
    }

    [Fact]
    public async Task FlowNodeBase_DiagnosticsCanBeEmittedAndCompleted()
    {
        var node = new DiagnosticReportingNode();
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        node.Diagnostics.LinkTo(
            diagnostics,
            new DataflowLinkOptions { PropagateCompletion = true });

        node.Report();
        node.Complete();

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe("node.ready.1");
        diagnostic.Level.ShouldBe(FlowDiagnosticLevel.Information);
        diagnostic.Message.ShouldBe("Node is ready.");
        diagnostic.Attributes["count"].ShouldBe(1);
        await diagnostics.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FlowNodeBase_DiagnosticsDeliverEveryMessageToSlowSubscribers()
    {
        var node = new DiagnosticReportingNode();
        var slowDiagnostics = new BufferBlock<FlowDiagnostic>(
            new DataflowBlockOptions { BoundedCapacity = 1 });
        node.Diagnostics.LinkTo(
            slowDiagnostics,
            new DataflowLinkOptions { PropagateCompletion = true });

        for (var value = 1; value <= 5; value++)
        {
            node.Report(value);
        }

        node.Complete();

        var names = new List<string>();
        while (await slowDiagnostics.OutputAvailableAsync().WaitAsync(TimeSpan.FromSeconds(5)))
        {
            while (slowDiagnostics.TryReceive(out var diagnostic))
            {
                names.Add(diagnostic.Name);
                await Task.Delay(20);
            }
        }

        names.ShouldBe([
            "node.ready.1",
            "node.ready.2",
            "node.ready.3",
            "node.ready.4",
            "node.ready.5"
        ]);
    }

    [Fact]
    public async Task FlowNodeBase_DiagnosticsCanBeReceivedDirectly()
    {
        var node = new DiagnosticReportingNode();
        var receive = node.Diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        node.Report();
        node.Complete();

        var diagnostic = await receive;

        diagnostic.Name.ShouldBe("node.ready.1");
        await node.Diagnostics.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task EventFlowNodeBase_EmitsChannelMetadata()
    {
        var node = new EventReportingNode();
        var events = new BufferBlock<FlowEvent>();
        node.Events.LinkTo(
            events,
            new DataflowLinkOptions { PropagateCompletion = true });

        node.Report();
        node.Complete();

        var flowEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        flowEvent.Type.ShouldBe("node.event");
        flowEvent.Channel.ShouldBe("demo.channel");
        await events.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RegistrationContract_CanUsePublicNodePorts()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(new PublicSequenceNodeRegistration());

        registry.TryGetFactory(new NodeType("test.public-sequence"), out var factory).ShouldBeTrue();
        factory.ShouldNotBeNull();
    }

    private static ApplicationRuntime BuildRuntime(
        RuntimeNodeFactoryRegistry registry,
        ApplicationDefinition definition)
    {
        var result = new ApplicationRuntimeBuilder(registry).Build(definition);
        result.IsSuccess.ShouldBeTrue(string.Join(Environment.NewLine, result.Errors.Select(e => e.Message)));
        return result.Runtime!;
    }

    private static System.Text.Json.JsonElement JsonValue<T>(T value)
        => System.Text.Json.JsonSerializer.SerializeToElement(value);

    private sealed class SequenceNode : SourceFlowNode<int>
    {
        public static RuntimeNode Create(RuntimeNodeFactoryContext context)
        {
            var node = new SequenceNode();
            return context.CreateNode(node)
                .Output("Output", node.OutputBlock)
                .Build();
        }

        public override async Task StartAsync(CancellationToken cancellationToken = default)
        {
            for (var value = 1; value <= 3; value++)
            {
                await SendOutputAsync(value, cancellationToken);
            }

            CompleteOutput();
        }
    }

    private sealed class DoubleNode : MapFlowNode<int, int>
    {
        public static RuntimeNode Create(RuntimeNodeFactoryContext context)
        {
            var node = new DoubleNode();
            return context.CreateNode(node)
                .Input("Input", node.InputBlock)
                .Output("Output", node.OutputBlock)
                .Build();
        }

        protected override ValueTask<int> MapAsync(
            int input,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(input * 2);
    }

    private sealed class CollectNode(List<int> values) : SinkFlowNode<int>
    {
        public static RuntimeNode Create(
            RuntimeNodeFactoryContext context,
            List<int> values)
        {
            var node = new CollectNode(values);
            return context.CreateNode(node)
                .Input("Input", node.InputBlock)
                .Build();
        }

        protected override ValueTask HandleAsync(
            int input,
            CancellationToken cancellationToken)
        {
            values.Add(input);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingSinkNode : SinkFlowNode<int>
    {
        public static RuntimeNode Create(RuntimeNodeFactoryContext context)
        {
            var node = new FailingSinkNode();
            return context.CreateNode(node)
                .Input("Input", node.InputBlock)
                .Build();
        }

        protected override ValueTask HandleAsync(
            int input,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("Nope.");
    }

    private sealed class SequenceNodeRegistration : IFlowNodeRegistration
    {
        public NodeType Type { get; } = new("test.sequence");

        public RuntimeNode Create(RuntimeNodeFactoryContext context)
            => SequenceNode.Create(context);
    }

    private sealed class ErrorReportingNode : FlowNodeBase
    {
        public void Report()
            => TryReportError(123, "Before fault.");

        protected override void OnNodeFaulted(Exception exception)
            => TryReportError(124, "Fault hook.", exception);
    }

    private sealed class FaultHookSourceNode : SourceFlowNode<int>
    {
        protected override void OnNodeFaulted(Exception exception)
            => throw new InvalidOperationException("Fault hook failed.");
    }

    private sealed class FaultHookSinkNode : SinkFlowNode<int>
    {
        protected override ValueTask HandleAsync(
            int input,
            CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        protected override void OnNodeFaulted(Exception exception)
            => throw new InvalidOperationException("Fault hook failed.");
    }

    private sealed class FaultHookMapNode : MapFlowNode<int, int>
    {
        protected override ValueTask<int> MapAsync(
            int input,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(input);

        protected override void OnNodeFaulted(Exception exception)
            => throw new InvalidOperationException("Fault hook failed.");
    }

    private sealed class DiagnosticReportingNode : FlowNodeBase
    {
        public void Report()
            => Report(1);

        public void Report(int count)
            => TryEmitDiagnostic(
                $"node.ready.{count}",
                message: "Node is ready.",
                attributes: new Dictionary<string, object?>
                {
                    ["count"] = count
                });
    }

    private sealed class EventReportingNode : EventFlowNodeBase
    {
        public void Report()
            => EmitEvent("node.event", channel: "demo.channel");
    }

    private sealed class PublicSequenceNode : SourceFlowNode<int>
    {
        public static RuntimeNode Create(RuntimeNodeFactoryContext context)
        {
            var node = new PublicSequenceNode();
            return context.CreateNode(node)
                .Output("Output", node.Output)
                .Build();
        }
    }

    private sealed class PublicSequenceNodeRegistration : IFlowNodeRegistration
    {
        public NodeType Type { get; } = new("test.public-sequence");

        public RuntimeNode Create(RuntimeNodeFactoryContext context)
            => PublicSequenceNode.Create(context);
    }
}
