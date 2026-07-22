using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

#pragma warning disable CS0618 // These tests intentionally verify the legacy compatibility surface.

namespace FluxFlow.Composition.Tests;

public sealed class CompositionRuntimeReliabilityTests
{
    [Fact]
    public async Task Shared_input_completes_only_after_every_upstream_completes()
    {
        var sources = new ManualSourceCollection();
        var collector = new StringCollector();
        var result = await new CompositionRuntimeBuilder(CreateRegistry())
            .BuildAsync(CreateFanInDefinition(), new TestServiceProvider().Add(sources).Add(collector));

        await using var runtime = result.Runtime.ShouldNotBeNull();
        await runtime.StartAsync();

        sources["first"].Emit("one").ShouldBeTrue();
        sources["first"].Complete();
        await sources["first"].Completion.WaitAsync(TimeSpan.FromSeconds(5));

        sources["second"].Emit("two").ShouldBeTrue();
        sources["second"].Complete();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        collector.Items.ShouldBe(["one", "two"]);
    }

    [Fact]
    public async Task Shared_input_faults_when_any_upstream_faults()
    {
        var sources = new ManualSourceCollection();
        var result = await new CompositionRuntimeBuilder(CreateRegistry())
            .BuildAsync(CreateFanInDefinition(), new TestServiceProvider().Add(sources));

        await using var runtime = result.Runtime.ShouldNotBeNull();
        await runtime.StartAsync();

        sources["first"].Fault(new InvalidOperationException("source failed"));
        sources["second"].Complete();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        exception.Message.ShouldBe("source failed");
    }

    [Fact]
    public async Task DisposeAsync_attempts_all_owned_cleanup_and_aggregates_failures()
    {
        var firstNode = new TrackingNode(throwOnDispose: true);
        var secondNode = new TrackingNode(throwOnDispose: false);
        var firstGraphLink = new TrackingDisposable(throwOnDispose: true);
        var secondGraphLink = new TrackingDisposable(throwOnDispose: false);
        var diagnosticLink = new TrackingDisposable(throwOnDispose: true);
        var events = new TrackingSourceBlock<FlowEvent>(diagnosticLink);
        var runtime = CompositionRuntime.Create(
            [
                ComposedNode.Create(firstNode, events: events),
                ComposedNode.Create(secondNode)
            ],
            [firstGraphLink, secondGraphLink],
            []);

        var exception = await Should.ThrowAsync<AggregateException>(
            async () => await runtime.DisposeAsync());

        exception.InnerExceptions.Count.ShouldBe(3);
        firstNode.DisposeCount.ShouldBe(1);
        secondNode.DisposeCount.ShouldBe(1);
        firstGraphLink.DisposeCount.ShouldBe(1);
        secondGraphLink.DisposeCount.ShouldBe(1);
        diagnosticLink.DisposeCount.ShouldBe(1);
    }

    private static CompositionNodeRegistry CreateRegistry()
        => new CompositionNodeRegistry()
            .Register(
                "test.manual-source",
                context =>
                {
                    var sources = (ManualSourceCollection)context.Services.GetService(
                        typeof(ManualSourceCollection))!;
                    var node = new ManualSourceNode();
                    sources.Add(context.ComponentName, node);
                    return ValueTask.FromResult(ComposedNode.Create(
                        node,
                        outputs: [CompositionPorts.Output<string>("Output", node.Output)]));
                },
                outputs: [CompositionPorts.Metadata<string>("Output")])
            .Register(
                "test.manual-sink",
                context =>
                {
                    var collector = (StringCollector?)context.Services.GetService(typeof(StringCollector))
                        ?? new StringCollector();
                    var node = new CollectSinkNode(collector);
                    return ValueTask.FromResult(ComposedNode.Create(
                        node,
                        inputs: [CompositionPorts.Input<string>("Input", node.Input)]));
                },
                inputs: [CompositionPorts.Metadata<string>("Input")]);

    private static CompositionDefinition CreateFanInDefinition()
        => CompositionDefinitionBuilder
            .Create()
            .Workflow("main", workflow => workflow
                .Node("first", "test.manual-source")
                .Node("second", "test.manual-source")
                .Node("sink", "test.manual-sink")
                .Link("first.Output", "sink.Input")
                .Link("second.Output", "sink.Input"))
            .Build();

    private sealed class ManualSourceCollection
    {
        private readonly Dictionary<string, ManualSourceNode> _sources = new(StringComparer.Ordinal);

        public ManualSourceNode this[string name] => _sources[name];

        public void Add(string name, ManualSourceNode source) => _sources.Add(name, source);
    }

    private sealed class ManualSourceNode : IFlowSource
    {
        private readonly BufferBlock<FlowMessage<string>> _output = new();

        public ISourceBlock<FlowMessage<string>> Output => _output;

        public Task Completion => _output.Completion;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public bool Emit(string value) => _output.Post(FlowMessage.Create(value));

        public void Complete() => _output.Complete();

        public void Fault(Exception exception) => ((IDataflowBlock)_output).Fault(exception);

        public async ValueTask DisposeAsync()
        {
            _output.Complete();
            try
            {
                await _output.Completion.ConfigureAwait(false);
            }
            catch
            {
                // Runtime completion remains the observable fault path.
            }
        }
    }

    private sealed class TrackingNode(bool throwOnDispose) : IFlowNode
    {
        public int DisposeCount { get; private set; }

        public Task Completion => Task.CompletedTask;

        public void Complete()
        {
        }

        public void Fault(Exception exception)
        {
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return throwOnDispose
                ? ValueTask.FromException(new InvalidOperationException("node cleanup failed"))
                : ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingDisposable(bool throwOnDispose) : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            if (throwOnDispose)
                throw new InvalidOperationException("link cleanup failed");
        }
    }

    private sealed class TrackingSourceBlock<T>(IDisposable link) : ISourceBlock<T>
    {
        public Task Completion => Task.CompletedTask;

        public void Complete()
        {
        }

        public void Fault(Exception exception)
        {
        }

        public IDisposable LinkTo(ITargetBlock<T> target, DataflowLinkOptions linkOptions) => link;

        public T? ConsumeMessage(
            DataflowMessageHeader messageHeader,
            ITargetBlock<T> target,
            out bool messageConsumed)
        {
            messageConsumed = false;
            return default;
        }

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
            => false;

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
        {
        }
    }
}
