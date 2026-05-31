using FluxFlow.Engine.Components;
using FluxFlow.Engine.Core;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class RuntimeLifecycleTests
{
    [Fact]
    public async Task OutputPort_DeliversEveryValueToEveryLinkedInput()
    {
        var source = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 1 });
        var output = new OutputPort<int>(
            new PortAddress("main", new NodeName("source"), new PortName("Output")),
            source);
        var fastTarget = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 10 });
        var slowTarget = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 1 });

        output.TryLinkTo(Input("fast", fastTarget), propagateCompletion: true, out var fastError)
            .ShouldNotBeNull();
        fastError.ShouldBeNull();
        output.TryLinkTo(Input("slow", slowTarget), propagateCompletion: true, out var slowError)
            .ShouldNotBeNull();
        slowError.ShouldBeNull();

        var fastRead = ReceiveAllAsync(fastTarget);
        var slowRead = ReceiveAllAsync(slowTarget, TimeSpan.FromMilliseconds(20));
        var producer = Task.Run(async () =>
        {
            for (var value = 1; value <= 5; value++)
            {
                await source.SendAsync(value).ConfigureAwait(false);
            }

            source.Complete();
        });

        await producer.WaitAsync(TimeSpan.FromSeconds(5));
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await fastRead.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe([1, 2, 3, 4, 5]);
        (await slowRead.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe([1, 2, 3, 4, 5]);
    }

    [Fact]
    public async Task OutputPort_DeliversOnlyMatchingValuesToConditionalLinks()
    {
        var source = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 1 });
        var output = new OutputPort<int>(
            new PortAddress("main", new NodeName("source"), new PortName("Output")),
            source);
        var evenTarget = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 10 });
        var oddTarget = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 10 });

        output.TryLinkTo(
            Input("even", evenTarget),
            propagateCompletion: true,
            new DelegateFlowPredicate<object?>(value => (int)value! % 2 == 0),
            out var evenError).ShouldNotBeNull();
        evenError.ShouldBeNull();
        output.TryLinkTo(
            Input("odd", oddTarget),
            propagateCompletion: true,
            new DelegateFlowPredicate<object?>(value => (int)value! % 2 != 0),
            out var oddError).ShouldNotBeNull();
        oddError.ShouldBeNull();

        var evenRead = ReceiveAllAsync(evenTarget);
        var oddRead = ReceiveAllAsync(oddTarget);

        for (var value = 1; value <= 5; value++)
        {
            await source.SendAsync(value);
        }

        source.Complete();
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await evenRead.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe([2, 4]);
        (await oddRead.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe([1, 3, 5]);
    }

    [Fact]
    public async Task OutputPort_DisposingLinkCancelsPendingSend()
    {
        var source = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 1 });
        var output = new OutputPort<int>(
            new PortAddress("main", new NodeName("source"), new PortName("Output")),
            source);
        var fullTarget = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 1 });
        await fullTarget.SendAsync(0);
        var link = output.TryLinkTo(Input("full", fullTarget), propagateCompletion: true, out var error);
        error.ShouldBeNull();
        link.ShouldNotBeNull();

        await source.SendAsync(1);
        await Task.Delay(100);

        link!.Dispose();
        source.Complete();

        await output.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task OutputPort_DeliversBufferedValueWhenLinkIsRegisteredAfterConstruction()
    {
        var source = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 2 });
        await source.SendAsync(42);
        source.Complete();
        var output = new OutputPort<int>(
            new PortAddress("main", new NodeName("source"), new PortName("Output")),
            source);
        await Task.Delay(100);
        var target = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 1 });

        output.TryLinkTo(Input("target", target), propagateCompletion: true, out var error)
            .ShouldNotBeNull();
        error.ShouldBeNull();

        await output.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        target.TryReceive(out var value).ShouldBeTrue();
        value.ShouldBe(42);
    }

    [Fact]
    public async Task OutputPort_DisposeAsync_CompletesPumpWhenSourceNeverCompletes()
    {
        var source = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 1 });
        var output = new OutputPort<int>(
            new PortAddress("main", new NodeName("source"), new PortName("Output")),
            source);

        await output.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task HostStartAsync_WhenLaterNodeFails_FaultsAndDisposesStartedNodes()
    {
        var startedNode = new TrackingNode();
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(new NodeType("test.started"), context => RuntimeNode.Create(context.Address, startedNode))
            .Register(new NodeType("test.fail-start"), context => RuntimeNode.Create(context.Address, new FailingStartNode()));
        using var host = FlowApplicationHost.Create(new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["main"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["started"] = new() { Type = new NodeType("test.started"), Phase = 0 },
                        ["fail"] = new() { Type = new NodeType("test.fail-start"), Phase = 1 }
                    }
                }
            }
        }, registry);

        var result = await host.StartAsync();

        result.IsSuccess.ShouldBeFalse();
        host.Runtime.ShouldBeNull();
        startedNode.Started.ShouldBeTrue();
        startedNode.Faulted.ShouldBeTrue();
        startedNode.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task HostStartAsync_WhenFaultCleanupThrows_PreservesStartFailureAndContinues()
    {
        var throwingFaultNode = new ThrowingFaultNode();
        var trackingNode = new TrackingNode();
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(new NodeType("test.throw-fault"), context => RuntimeNode.Create(context.Address, throwingFaultNode))
            .Register(new NodeType("test.tracking"), context => RuntimeNode.Create(context.Address, trackingNode))
            .Register(new NodeType("test.fail-start"), context => RuntimeNode.Create(context.Address, new FailingStartNode()));
        using var host = FlowApplicationHost.Create(new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["main"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["throwFault"] = new() { Type = new NodeType("test.throw-fault"), Phase = 0 },
                        ["tracking"] = new() { Type = new NodeType("test.tracking"), Phase = 0 },
                        ["fail"] = new() { Type = new NodeType("test.fail-start"), Phase = 1 }
                    }
                }
            }
        }, registry);

        var result = await host.StartAsync();

        result.IsSuccess.ShouldBeFalse();
        host.LastException.ShouldNotBeNull();
        host.LastException.Message.ShouldBe("Start failed.");
        throwingFaultNode.FaultCalled.ShouldBeTrue();
        trackingNode.Faulted.ShouldBeTrue();
        host.Runtime.ShouldBeNull();
    }

    [Fact]
    public async Task HostStartAsync_WhenDisposeAfterStartFailureThrows_ReturnsStartFailureAndClearsRuntime()
    {
        var throwingDisposeNode = new ThrowingDisposeAsyncNode();
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(new NodeType("test.throw-dispose-async"), context => RuntimeNode.Create(context.Address, throwingDisposeNode))
            .Register(new NodeType("test.fail-start"), context => RuntimeNode.Create(context.Address, new FailingStartNode()));
        using var host = FlowApplicationHost.Create(new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["main"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["dispose"] = new() { Type = new NodeType("test.throw-dispose-async"), Phase = 0 },
                        ["fail"] = new() { Type = new NodeType("test.fail-start"), Phase = 1 }
                    }
                }
            }
        }, registry);

        var result = await host.StartAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.Message.Contains("Start failed."));
        result.Errors.ShouldContain(error => error.Message.Contains("cleanup failed"));
        host.LastException.ShouldNotBeNull();
        host.LastException.Message.ShouldBe("Start failed.");
        host.Runtime.ShouldBeNull();
        throwingDisposeNode.DisposeAsyncCount.ShouldBe(1);
    }

    [Fact]
    public async Task HostStartAsync_WhenCanceled_ClearsRuntimeAndStopsWithoutFaultingStartedNodes()
    {
        var startedNode = new TrackingNode();
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(new NodeType("test.started"), context => RuntimeNode.Create(context.Address, startedNode))
            .Register(new NodeType("test.cancel-start"), context => RuntimeNode.Create(context.Address, new CancelingStartNode()));
        using var host = FlowApplicationHost.Create(new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["main"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["started"] = new() { Type = new NodeType("test.started"), Phase = 0 },
                        ["cancel"] = new() { Type = new NodeType("test.cancel-start"), Phase = 1 }
                    }
                }
            }
        }, registry);

        await Should.ThrowAsync<OperationCanceledException>(async () => await host.StartAsync());

        host.State.ShouldBe(FlowApplicationHostState.Stopped);
        host.Runtime.ShouldBeNull();
        host.LastException.ShouldBeNull();
        startedNode.Started.ShouldBeTrue();
        startedNode.Faulted.ShouldBeFalse();
    }

    [Fact]
    public async Task ApplicationRuntimeStartAsync_WhenCanceled_DoesNotFaultStartedNodes()
    {
        var startedNode = new TrackingNode();
        var runtime = new ApplicationRuntime(
            [],
            [
                new Workflow(
                    new WorkflowName("main"),
                    [
                        RuntimeNode.Create(new NodeAddress("main", new NodeName("started")), startedNode, phase: 0),
                        RuntimeNode.Create(new NodeAddress("main", new NodeName("cancel")), new CancelingStartNode(), phase: 1)
                    ],
                    [],
                    [])
            ],
            []);

        await Should.ThrowAsync<OperationCanceledException>(async () => await runtime.StartAsync());

        runtime.State.ShouldBe(ApplicationState.Stopped);
        runtime.Workflows.Single().State.ShouldBe(WorkflowState.Stopped);
        startedNode.Started.ShouldBeTrue();
        startedNode.Faulted.ShouldBeFalse();

        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task ApplicationRuntimeDisposeAsync_WhenMultiSourceCompletionLinkIsDisposed_DoesNotFaultInput()
    {
        var sink = new CompletionTrackingSinkNode();
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(new NodeType("test.passive-source"), context =>
            {
                var node = new PassiveSourceNode();
                return context.CreateNode(node)
                    .Output("Output", node.Output)
                    .Build();
            })
            .Register(new NodeType("test.completion-sink"), context =>
                context.CreateNode(sink)
                    .Input("Input", sink.Input)
                    .Build());
        var result = new ApplicationRuntimeBuilder(registry).Build(new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["main"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["sourceA"] = new() { Type = new NodeType("test.passive-source") },
                        ["sourceB"] = new() { Type = new NodeType("test.passive-source") },
                        ["sink"] = new()
                        {
                            Type = new NodeType("test.completion-sink"),
                            Ports =
                            {
                                ["Input"] = JsonValue(new[] { "sourceA.Output", "sourceB.Output" })
                            }
                        }
                    }
                }
            }
        });
        result.IsSuccess.ShouldBeTrue(string.Join(Environment.NewLine, result.Errors.Select(error => error.Message)));

        await result.Runtime!.DisposeAsync();
        await Task.Delay(100);

        sink.Input.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task RuntimeAndWorkflowDiagnostics_CollectAndEnrichNodeDiagnostics()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(new NodeType("test.diagnostic"), DiagnosticNode.Create);
        var result = new ApplicationRuntimeBuilder(registry).Build(new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["main"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["source"] = new()
                        {
                            Type = new NodeType("test.diagnostic"),
                            Phase = 3
                        }
                    }
                }
            }
        });
        result.IsSuccess.ShouldBeTrue(string.Join(Environment.NewLine, result.Errors.Select(error => error.Message)));
        await using var runtime = result.Runtime!;
        var runtimeDiagnostics = new BufferBlock<RuntimeFlowDiagnostic>();
        var workflowDiagnostics = new BufferBlock<RuntimeFlowDiagnostic>();
        runtime.Diagnostics.LinkTo(
            runtimeDiagnostics,
            new DataflowLinkOptions { PropagateCompletion = true });
        runtime.Workflows.Single().Diagnostics.LinkTo(
            workflowDiagnostics,
            new DataflowLinkOptions { PropagateCompletion = true });

        await runtime.StartAsync();

        var runtimeDiagnostic = await runtimeDiagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var workflowDiagnostic = await workflowDiagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        runtimeDiagnostic.Name.ShouldBe("diagnostic.node.started");
        runtimeDiagnostic.Message.ShouldBe("Diagnostic node started.");
        runtimeDiagnostic.NodeAddress.Scope.ShouldBe("main");
        runtimeDiagnostic.NodeAddress.Node.Value.ShouldBe("source");
        runtimeDiagnostic.NodeId.ShouldBe(workflowDiagnostic.NodeId);
        runtimeDiagnostic.NodeType.ShouldBe(new NodeType("test.diagnostic"));
        runtimeDiagnostic.NodePhase.ShouldBe(3);
        runtimeDiagnostic.Attributes["phase"].ShouldBe(3);
        workflowDiagnostic.Name.ShouldBe(runtimeDiagnostic.Name);
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeDiagnostics.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await workflowDiagnostics.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task HostDiagnostics_CanBeSubscribedBeforeStartAsync()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(new NodeType("test.diagnostic"), DiagnosticNode.Create);
        using var host = FlowApplicationHost.Create(new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["main"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["source"] = new()
                        {
                            Type = new NodeType("test.diagnostic"),
                            Phase = 3
                        }
                    }
                }
            }
        }, registry);
        var diagnostics = new BufferBlock<RuntimeFlowDiagnostic>();
        host.Diagnostics.LinkTo(
            diagnostics,
            new DataflowLinkOptions { PropagateCompletion = true });

        var result = await host.StartAsync();

        result.IsSuccess.ShouldBeTrue(string.Join(Environment.NewLine, result.Errors.Select(error => error.Message)));
        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe("diagnostic.node.started");
        diagnostic.NodeAddress.Scope.ShouldBe("main");
        diagnostic.NodeAddress.Node.Value.ShouldBe("source");
        diagnostic.NodeType.ShouldBe(new NodeType("test.diagnostic"));
    }

    [Fact]
    public async Task ApplicationRuntimeDisposeAsync_DisposesSyncOnlyResources()
    {
        var node = new TrackingNode();
        var runtimeNode = RuntimeNode.Create(
            new NodeAddress(WellKnownScopes.Resources, new NodeName("resource")),
            node);
        var runtime = new ApplicationRuntime(
            [runtimeNode],
            [],
            [runtimeNode]);

        await runtime.DisposeAsync();

        node.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public void ApplicationRuntimeDispose_WhenOneWorkflowNodeThrows_DisposesRemainingNodesAndResources()
    {
        var throwingNode = new ThrowingDisposeNode();
        var workflowTrackingNode = new TrackingNode();
        var resourceNode = new TrackingNode();
        var throwingRuntimeNode = RuntimeNode.Create(
            new NodeAddress("main", new NodeName("throw")),
            throwingNode);
        var trackingRuntimeNode = RuntimeNode.Create(
            new NodeAddress("main", new NodeName("track")),
            workflowTrackingNode);
        var resourceRuntimeNode = RuntimeNode.Create(
            new NodeAddress(WellKnownScopes.Resources, new NodeName("resource")),
            resourceNode);
        var runtime = new ApplicationRuntime(
            [resourceRuntimeNode],
            [new Workflow(new WorkflowName("main"), [throwingRuntimeNode, trackingRuntimeNode], [], [throwingRuntimeNode])],
            [resourceRuntimeNode]);

        var exception = Should.Throw<AggregateException>(runtime.Dispose);

        exception.InnerExceptions.ShouldNotBeEmpty();
        throwingNode.DisposeCount.ShouldBe(1);
        workflowTrackingNode.DisposeCount.ShouldBe(1);
        resourceNode.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task ApplicationRuntimeDisposeAsync_WhenOneWorkflowNodeThrows_DisposesRemainingNodesAndResources()
    {
        var throwingNode = new ThrowingDisposeAsyncNode();
        var workflowTrackingNode = new TrackingNode();
        var resourceNode = new TrackingNode();
        var throwingRuntimeNode = RuntimeNode.Create(
            new NodeAddress("main", new NodeName("throw")),
            throwingNode);
        var trackingRuntimeNode = RuntimeNode.Create(
            new NodeAddress("main", new NodeName("track")),
            workflowTrackingNode);
        var resourceRuntimeNode = RuntimeNode.Create(
            new NodeAddress(WellKnownScopes.Resources, new NodeName("resource")),
            resourceNode);
        var runtime = new ApplicationRuntime(
            [resourceRuntimeNode],
            [new Workflow(new WorkflowName("main"), [throwingRuntimeNode, trackingRuntimeNode], [], [throwingRuntimeNode])],
            [resourceRuntimeNode]);

        var exception = await Should.ThrowAsync<AggregateException>(async () => await runtime.DisposeAsync());

        exception.InnerExceptions.ShouldNotBeEmpty();
        throwingNode.DisposeAsyncCount.ShouldBe(1);
        workflowTrackingNode.DisposeCount.ShouldBe(1);
        resourceNode.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public void Build_WhenFactoryFails_DisposesAsyncOnlyCreatedNodes()
    {
        var asyncNode = new AsyncOnlyNode();
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(new NodeType("test.async"), context => RuntimeNode.Create(context.Address, asyncNode))
            .Register(new NodeType("test.fail-build"), _ => throw new InvalidOperationException("Build failed."));
        var builder = new ApplicationRuntimeBuilder(registry);

        var result = builder.Build(new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["main"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["async"] = new() { Type = new NodeType("test.async") },
                        ["fail"] = new() { Type = new NodeType("test.fail-build") }
                    }
                }
            }
        });

        result.IsSuccess.ShouldBeFalse();
        asyncNode.DisposeAsyncCount.ShouldBe(1);
    }

    private static InputPort<int> Input(string nodeName, ITargetBlock<int> target)
        => new(
            new PortAddress("main", new NodeName(nodeName), new PortName("Input")),
            target);

    private static System.Text.Json.JsonElement JsonValue<T>(T value)
        => System.Text.Json.JsonSerializer.SerializeToElement(value);

    private static async Task<IReadOnlyList<T>> ReceiveAllAsync<T>(
        IReceivableSourceBlock<T> source,
        TimeSpan? delayAfterReceive = null)
    {
        var values = new List<T>();
        while (await source.OutputAvailableAsync().ConfigureAwait(false))
        {
            while (source.TryReceive(out var value))
            {
                values.Add(value);
                if (delayAfterReceive is { } delay)
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                }
            }
        }

        return values;
    }

    private sealed class TrackingNode : IFlowNode, IDisposable
    {
        private readonly BufferBlock<FlowError> _errors = new();
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FlowNodeId Id { get; } = FlowNodeId.New();
        public ISourceBlock<FlowError> Errors => _errors;
        public Task Completion => _completion.Task;
        public bool Started { get; private set; }
        public bool Faulted { get; private set; }
        public int DisposeCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception)
        {
            Faulted = true;
            _completion.TrySetException(exception);
        }

        public void Dispose()
        {
            DisposeCount++;
            _errors.Complete();
        }
    }

    private sealed class FailingStartNode : IFlowNode
    {
        private readonly BufferBlock<FlowError> _errors = new();
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FlowNodeId Id { get; } = FlowNodeId.New();
        public ISourceBlock<FlowError> Errors => _errors;
        public Task Completion => _completion.Task;

        public Task StartAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Start failed.");

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class ThrowingFaultNode : IFlowNode, IDisposable
    {
        private readonly BufferBlock<FlowError> _errors = new();
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FlowNodeId Id { get; } = FlowNodeId.New();
        public ISourceBlock<FlowError> Errors => _errors;
        public Task Completion => _completion.Task;
        public bool FaultCalled { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception)
        {
            FaultCalled = true;
            throw new InvalidOperationException("Fault cleanup failed.");
        }

        public void Dispose()
            => _errors.Complete();
    }

    private sealed class CancelingStartNode : IFlowNode
    {
        private readonly BufferBlock<FlowError> _errors = new();
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FlowNodeId Id { get; } = FlowNodeId.New();
        public ISourceBlock<FlowError> Errors => _errors;
        public Task Completion => _completion.Task;

        public Task StartAsync(CancellationToken cancellationToken = default)
            => throw new OperationCanceledException(cancellationToken);

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class PassiveSourceNode : IFlowNode, IDisposable
    {
        private readonly BufferBlock<FlowError> _errors = new();
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FlowNodeId Id { get; } = FlowNodeId.New();
        public ISourceBlock<FlowError> Errors => _errors;
        public BufferBlock<int> Output { get; } = new(new DataflowBlockOptions { BoundedCapacity = 1 });
        public Task Completion => _completion.Task;

        public void Complete()
        {
            Output.Complete();
            _completion.TrySetResult();
        }

        public void Fault(Exception exception)
        {
            ((IDataflowBlock)Output).Fault(exception);
            _completion.TrySetException(exception);
        }

        public void Dispose() => _errors.Complete();
    }

    private sealed class CompletionTrackingSinkNode : IFlowNode, IDisposable
    {
        private readonly BufferBlock<FlowError> _errors = new();
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FlowNodeId Id { get; } = FlowNodeId.New();
        public ISourceBlock<FlowError> Errors => _errors;
        public BufferBlock<int> Input { get; } = new(new DataflowBlockOptions { BoundedCapacity = 1 });
        public Task Completion => _completion.Task;

        public void Complete()
        {
            Input.Complete();
            _completion.TrySetResult();
        }

        public void Fault(Exception exception)
        {
            ((IDataflowBlock)Input).Fault(exception);
            _completion.TrySetException(exception);
        }

        public void Dispose() => _errors.Complete();
    }

    private sealed class DiagnosticNode : FlowNodeBase
    {
        public static RuntimeNode Create(RuntimeNodeFactoryContext context)
        {
            var node = new DiagnosticNode();
            return context.CreateNode(node).Build();
        }

        public override Task StartAsync(CancellationToken cancellationToken = default)
        {
            TryEmitDiagnostic(
                "diagnostic.node.started",
                message: "Diagnostic node started.",
                attributes: new Dictionary<string, object?>
                {
                    ["phase"] = 3
                });
            Complete();
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingDisposeNode : IFlowNode, IDisposable
    {
        private readonly BufferBlock<FlowError> _errors = new();
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FlowNodeId Id { get; } = FlowNodeId.New();
        public ISourceBlock<FlowError> Errors => _errors;
        public Task Completion => _completion.Task;
        public int DisposeCount { get; private set; }

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception) => _completion.TrySetException(exception);

        public void Dispose()
        {
            DisposeCount++;
            throw new InvalidOperationException("Dispose failed.");
        }
    }

    private sealed class ThrowingDisposeAsyncNode : IFlowNode, IAsyncDisposable
    {
        private readonly BufferBlock<FlowError> _errors = new();
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FlowNodeId Id { get; } = FlowNodeId.New();
        public ISourceBlock<FlowError> Errors => _errors;
        public Task Completion => _completion.Task;
        public int DisposeAsyncCount { get; private set; }

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception) => _completion.TrySetException(exception);

        public ValueTask DisposeAsync()
        {
            DisposeAsyncCount++;
            throw new InvalidOperationException("Dispose failed.");
        }
    }

    private sealed class AsyncOnlyNode : IFlowNode, IAsyncDisposable
    {
        private readonly BufferBlock<FlowError> _errors = new();
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FlowNodeId Id { get; } = FlowNodeId.New();
        public ISourceBlock<FlowError> Errors => _errors;
        public Task Completion => _completion.Task;
        public int DisposeAsyncCount { get; private set; }

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception) => _completion.TrySetException(exception);

        public ValueTask DisposeAsync()
        {
            DisposeAsyncCount++;
            _errors.Complete();
            return ValueTask.CompletedTask;
        }
    }
}
