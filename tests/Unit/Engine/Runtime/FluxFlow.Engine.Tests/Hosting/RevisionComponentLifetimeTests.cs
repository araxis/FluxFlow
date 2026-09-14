using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests.Hosting;

public sealed class RevisionComponentLifetimeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Hosted_output_failure_releases_producer_and_faults_revision(bool source, bool staged)
    {
        var address = ApplicationAddress.Parse("main.producer.Output");
        var events = ApplicationAddress.Parse("main.producer.Events");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IFlowComponent producer = source ? new ProducingSource(start.Task, finished) : new ProducingNode(start.Task, finished);
        var sibling = new LifetimeNode();
        ComponentInstance[] components = [new(producer), new(sibling)];
        var ports = new ApplicationPortRuntimeBuilder().AddOutput(events, discardUnmatched: true)
            .AddOutput(address, capacity: 2).Build();
        Exception? outputFailure = null;
        await using var builder = ports.CreateRevision("strict-output");
        var revision = builder.AttachOutput(events, producer.Ports.Events!)
            .AttachOutput(address, producer.Ports.Outputs["Output"]).Build();
        var plan = staged ? TestDrainPlan(components, new HashSet<ApplicationAddress> { events, address }) : null;
        var candidate = new ApplicationRuntimeRevisionCandidate(components, revision, [], plan,
            () => ValueTask.CompletedTask, null);
        try
        {
            await candidate.ActivateAsync();
            await using var observation = (await ports.ObserveAsync<FlowValue>(address)).Observation.ShouldNotBeNull();
            if (producer is ProducingNode node)
                (await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
            start.SetResult();
            var failure = await Should.ThrowAsync<InvalidOperationException>(() => observation.Completion.WaitAsync(Deadline));
            outputFailure = failure;
            failure.Message.ShouldContain("No route accepted");
            failure.Message.ShouldContain(address.Value);
            (await Should.ThrowAsync<InvalidOperationException>(() => candidate.Completion.WaitAsync(Deadline)))
                .ShouldBeSameAs(failure);
            await finished.Task.WaitAsync(Deadline);
            (await Should.ThrowAsync<InvalidOperationException>(() => producer.Completion.WaitAsync(Deadline)))
                .ShouldBeSameAs(failure);
            sibling.FaultCalls.ShouldBe(1);
            sibling.Failure.ShouldBeSameAs(failure);
            var drainFailure = await Should.ThrowAsync<Exception>(() => candidate.DrainAsync().AsTask().WaitAsync(Deadline));
            drainFailure.GetBaseException().ShouldBeSameAs(failure);
        }
        finally
        {
            start.TrySetResult();
            producer.Fault(new OperationCanceledException("test cleanup"));
            await candidate.DisposeAsync().AsTask().WaitAsync(Deadline);
            (await Should.ThrowAsync<InvalidOperationException>(() => ports.DisposeAsync().AsTask().WaitAsync(Deadline)))
                .ShouldBeSameAs(outputFailure);
        }
        sibling.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Disposed_revision_does_not_observe_later_failure_of_shared_output()
    {
        var address = ApplicationAddress.Parse("main.node.Output");
        var ports = new ApplicationPortRuntimeBuilder().AddOutput(address).Build();
        var retiredNode = new LifetimeNode();
        var retiredSource = new BufferBlock<FlowMessage>();
        await using var retiredBuilder = ports.CreateRevision("retired");
        var retired = new ApplicationRuntimeRevisionCandidate([new(retiredNode)],
            retiredBuilder.AttachOutput(address, retiredSource).Build(), [], null, () => ValueTask.CompletedTask, null);
        var activeNode = new LifetimeNode();
        var activeSource = new BufferBlock<FlowMessage>();
        await using var activeBuilder = ports.CreateRevision("active");
        var active = new ApplicationRuntimeRevisionCandidate([new(activeNode)],
            activeBuilder.AttachOutput(address, activeSource).Build(), [], null, () => ValueTask.CompletedTask, null);
        Exception? failure = null;
        try
        {
            await retired.ActivateAsync();
            retiredSource.Complete();
            await retired.DrainAsync().AsTask().WaitAsync(Deadline);
            await retired.DisposeAsync().AsTask().WaitAsync(Deadline);
            retired.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            await active.ActivateAsync();
            await using var observation = (await ports.ObserveAsync<FlowValue>(address)).Observation.ShouldNotBeNull();
            (await activeSource.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
            failure = await Should.ThrowAsync<InvalidOperationException>(() => observation.Completion.WaitAsync(Deadline));
            (await Should.ThrowAsync<InvalidOperationException>(() => active.Completion.WaitAsync(Deadline)))
                .ShouldBeSameAs(failure);
            activeNode.FaultCalls.ShouldBe(1);
            activeNode.Failure.ShouldBeSameAs(failure);
        }
        finally
        {
            activeSource.Complete();
            retiredSource.Complete();
            await active.DisposeAsync().AsTask().WaitAsync(Deadline);
            await retired.DisposeAsync().AsTask().WaitAsync(Deadline);
            (await Should.ThrowAsync<InvalidOperationException>(() => ports.DisposeAsync().AsTask().WaitAsync(Deadline)))
                .ShouldBeSameAs(failure);
        }
        retiredNode.FaultCalls.ShouldBe(0);
        retiredNode.DisposeCalls.ShouldBe(1);
        activeNode.DisposeCalls.ShouldBe(1);
    }

    private sealed class ProducingSource(Task start, TaskCompletionSource finished) : FlowSource(new() { OutputCapacity = 2 })
    {
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                await start.WaitAsync(cancellationToken);
                for (var value = 0; ; value++) await EmitAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), cancellationToken);
            }
            finally { finished.TrySetResult(); }
        }
    }

    private sealed class ProducingNode(Task start, TaskCompletionSource finished) : FlowNode(new() { OutputCapacity = 2 })
    {
        protected override async Task ProcessAsync(FlowMessage message)
        {
            try
            {
                await start.WaitAsync(Stopping);
                for (var value = 0; ; value++) await EmitAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), Stopping);
            }
            finally { finished.TrySetResult(); }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Revision_candidate_startup_failure_faults_every_component(int failingIndex)
    {
        var failure = new InvalidOperationException("startup failed");
        var nodes = Enumerable.Range(0, 3)
            .Select(index => new LifetimeNode(index == failingIndex ? failure : null, throwOnFault: index == 1)).ToArray();
        await using var ports = new ApplicationPortRuntimeBuilder().Build();
        await using var builder = ports.CreateRevision("starting");
        var candidate = new ApplicationRuntimeRevisionCandidate(nodes.Select(node => new ComponentInstance(node)).ToArray(),
            builder.Build(), [], null, () => ValueTask.CompletedTask, null);
        try
        {
            (await Should.ThrowAsync<InvalidOperationException>(() => candidate.ActivateAsync().AsTask().WaitAsync(Deadline)))
                .ShouldBeSameAs(failure);
            nodes.Select(node => node.StartCalls).ShouldBe(Enumerable.Range(0, 3).Select(index => index <= failingIndex ? 1 : 0));
            nodes.ShouldAllBe(node => node.FaultCalls == 1 && ReferenceEquals(node.Failure, failure));
            nodes.ShouldAllBe(node => node.DisposeCalls == 0);
            (await Should.ThrowAsync<InvalidOperationException>(() => candidate.Completion.WaitAsync(Deadline)))
                .ShouldBeSameAs(failure);
        }
        finally { await candidate.DisposeAsync(); }
        nodes.ShouldAllBe(node => node.DisposeCalls == 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revision_candidate_joins_additional_work_and_preserves_first_failure(bool descriptorFails)
    {
        var failure = new InvalidOperationException("first failure");
        var nodes = new[] { new LifetimeNode(), new LifetimeNode(throwOnFault: true), new LifetimeNode() };
        var originWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acceptedWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var ports = new ApplicationPortRuntimeBuilder().Build();
        await using var builder = ports.CreateRevision("active");
        var candidate = new ApplicationRuntimeRevisionCandidate(
            [new(nodes[0], completion: originWork.Task), new(nodes[1]), new(nodes[2], completion: acceptedWork.Task)],
            builder.Build(), [], null, () => ValueTask.CompletedTask, null);
        try
        {
            await candidate.ActivateAsync();
            if (descriptorFails) originWork.TrySetException(failure);
            else nodes[0].Fail(failure);
            await nodes[2].Faulted.Task.WaitAsync(Deadline);
            nodes.ShouldAllBe(node => node.FaultCalls == 1 && ReferenceEquals(node.Failure, failure));
            candidate.Completion.IsCompleted.ShouldBeFalse();
            nodes.ShouldAllBe(node => node.DisposeCalls == 0);
            originWork.TrySetResult();
            candidate.Completion.IsCompleted.ShouldBeFalse();
            acceptedWork.TrySetException(new ArgumentException("later work failed"));
            (await Should.ThrowAsync<InvalidOperationException>(() => candidate.Completion.WaitAsync(Deadline)))
                .ShouldBeSameAs(failure);
        }
        finally
        {
            originWork.TrySetResult();
            acceptedWork.TrySetResult();
            await candidate.DisposeAsync();
        }
        nodes.ShouldAllBe(node => node.DisposeCalls == 1);
    }

    [Fact]
    public async Task Revision_candidate_disposes_all_components_and_releases_ports_once()
    {
        var nodeFailure = new InvalidOperationException("node cleanup");
        var callbackFailure = new ArgumentException("component cleanup");
        var releaseFailure = new ApplicationException("port cleanup");
        var nodes = new[] { new LifetimeNode(disposeFailure: nodeFailure), new LifetimeNode(), new LifetimeNode() };
        var callbacks = 0;
        var releases = 0;
        await using var ports = new ApplicationPortRuntimeBuilder().Build();
        await using var builder = ports.CreateRevision("owned");
        var candidate = new ApplicationRuntimeRevisionCandidate(
            [new(nodes[0]), new(nodes[1], disposeAsync: () => { callbacks++; return ValueTask.FromException(callbackFailure); }), new(nodes[2])],
            builder.Build(), [], null,
            () => { releases++; return ValueTask.FromException(releaseFailure); }, null);
        await candidate.ActivateAsync();
        await candidate.DrainAsync().AsTask().WaitAsync(Deadline);
        candidate.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        var failure = await Should.ThrowAsync<AggregateException>(() => candidate.DisposeAsync().AsTask().WaitAsync(Deadline));
        failure.Flatten().InnerExceptions.ShouldBe([nodeFailure, callbackFailure, releaseFailure], ignoreOrder: true);
        nodes.ShouldAllBe(node => node.DisposeCalls == 1);
        callbacks.ShouldBe(1);
        releases.ShouldBe(1);
        await candidate.DisposeAsync();
        nodes.ShouldAllBe(node => node.DisposeCalls == 1);
        releases.ShouldBe(1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Revision_candidate_drain_callback_failure_faults_every_component(bool staged, bool finalCompletion)
    {
        var failure = new InvalidOperationException("drain callback failed");
        var source = new DrainingSource(stopFailure: finalCompletion ? null : failure,
            completeFailure: finalCompletion ? failure : null);
        var siblings = new[] { new LifetimeNode(throwOnFault: true), new LifetimeNode() };
        ComponentInstance[] components = [new(source), .. siblings.Select(node => new ComponentInstance(node))];
        await using var ports = new ApplicationPortRuntimeBuilder().Build();
        await using var builder = ports.CreateRevision("draining");
        var plan = staged ? TestDrainPlan(components) : null;
        var candidate = new ApplicationRuntimeRevisionCandidate(components, builder.Build(), [], plan,
            () => ValueTask.CompletedTask, null);
        try
        {
            await candidate.ActivateAsync();
            (await Should.ThrowAsync<InvalidOperationException>(() => candidate.DrainAsync().AsTask().WaitAsync(Deadline)))
                .ShouldBeSameAs(failure);
            source.StopCalls.ShouldBe(1);
            source.CompleteCalls.ShouldBe(finalCompletion ? 1 : 0);
            source.FaultCalls.ShouldBe(1);
            source.Failure.ShouldBeSameAs(failure);
            siblings.ShouldAllBe(node => node.FaultCalls == 1 && ReferenceEquals(node.Failure, failure));
            (await Should.ThrowAsync<InvalidOperationException>(() => candidate.Completion.WaitAsync(Deadline)))
                .ShouldBeSameAs(failure);
            source.DisposeCalls.ShouldBe(0);
            siblings.ShouldAllBe(node => node.DisposeCalls == 0);
        }
        finally { await candidate.DisposeAsync(); }
        source.DisposeCalls.ShouldBe(1);
        siblings.ShouldAllBe(node => node.DisposeCalls == 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revision_candidate_cancelled_drain_faults_every_component(bool staged)
    {
        var source = new DrainingSource(holdProduction: true);
        var sibling = new LifetimeNode(throwOnFault: true);
        ComponentInstance[] components = [new(source), new(sibling)];
        await using var ports = new ApplicationPortRuntimeBuilder().Build();
        await using var builder = ports.CreateRevision("cancelled");
        var plan = staged ? TestDrainPlan(components) : null;
        var candidate = new ApplicationRuntimeRevisionCandidate(components, builder.Build(), [], plan,
            () => ValueTask.CompletedTask, null);
        using var cancellation = new CancellationTokenSource();
        try
        {
            await candidate.ActivateAsync();
            var drain = candidate.DrainAsync(cancellation.Token).AsTask();
            await source.Stopped.Task.WaitAsync(Deadline);
            drain.IsCompleted.ShouldBeFalse();
            source.CompleteCalls.ShouldBe(0);
            cancellation.Cancel();
            var failure = await Should.ThrowAsync<OperationCanceledException>(() => drain.WaitAsync(Deadline));
            failure.CancellationToken.ShouldBe(cancellation.Token);
            drain.IsCanceled.ShouldBeTrue();
            source.FaultCalls.ShouldBe(1);
            source.Failure.ShouldBeAssignableTo<OperationCanceledException>().ShouldNotBeNull()
                .CancellationToken.ShouldBe(cancellation.Token);
            sibling.FaultCalls.ShouldBe(1);
            sibling.Failure.ShouldBeSameAs(source.Failure);
            (await Should.ThrowAsync<OperationCanceledException>(() => candidate.Completion.WaitAsync(Deadline)))
                .CancellationToken.ShouldBe(cancellation.Token);
            candidate.Completion.IsCanceled.ShouldBeTrue();
            source.DisposeCalls.ShouldBe(0);
            sibling.DisposeCalls.ShouldBe(0);
        }
        finally
        {
            cancellation.Cancel();
            source.ProductionFinished.TrySetResult();
            await candidate.DisposeAsync();
        }
        source.DisposeCalls.ShouldBe(1);
        sibling.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Revision_candidate_staged_drain_waits_for_production_before_completing()
    {
        var source = new DrainingSource(holdProduction: true);
        ComponentInstance[] components = [new(source)];
        await using var ports = new ApplicationPortRuntimeBuilder().Build();
        await using var builder = ports.CreateRevision("graceful");
        var plan = TestDrainPlan(components);
        var candidate = new ApplicationRuntimeRevisionCandidate(components, builder.Build(), [], plan,
            () => ValueTask.CompletedTask, null);
        try
        {
            await candidate.ActivateAsync();
            var drain = candidate.DrainAsync().AsTask();
            await source.Stopped.Task.WaitAsync(Deadline);
            drain.IsCompleted.ShouldBeFalse();
            candidate.Completion.IsCompleted.ShouldBeFalse();
            source.CompleteCalls.ShouldBe(0);
            source.ProductionFinished.TrySetResult();
            await drain.WaitAsync(Deadline);
            candidate.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            source.StopCalls.ShouldBe(1);
            source.CompleteCalls.ShouldBe(1);
            source.FaultCalls.ShouldBe(0);
            source.DisposeCalls.ShouldBe(0);
        }
        finally
        {
            source.ProductionFinished.TrySetResult();
            await candidate.DisposeAsync();
        }
        source.DisposeCalls.ShouldBe(1);
    }

    private static ApplicationRuntimeDrainPlan TestDrainPlan(ComponentInstance[] components,
        IReadOnlySet<ApplicationAddress>? outputs = null)
        => new([
            new(components.Select(component => new ApplicationRuntimeDrainStep(component, component.Node is IFlowSource)).ToArray(),
                new HashSet<ApplicationAddress>(), new HashSet<ApplicationAddress>()),
            new(components.Where(component => component.Node is IFlowSource)
                    .Select(component => new ApplicationRuntimeDrainStep(component, false)).ToArray(),
                new HashSet<ApplicationAddress>(), outputs ?? new HashSet<ApplicationAddress>())]);

    private sealed class DrainingSource(Exception? stopFailure = null, Exception? completeFailure = null,
        bool holdProduction = false) : IFlowSource
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ProductionFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Completion => _completion.Task;
        public Task ProductionCompletion => ProductionFinished.Task;
        public int StopCalls { get; private set; }
        public int CompleteCalls { get; private set; }
        public int FaultCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public Exception? Failure { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void StopProduction()
        {
            StopCalls++;
            Stopped.TrySetResult();
            if (stopFailure is not null) throw stopFailure;
            if (!holdProduction) ProductionFinished.TrySetResult();
        }
        public void Complete()
        {
            CompleteCalls++;
            if (completeFailure is not null) throw completeFailure;
            _completion.TrySetResult();
        }
        public void Fault(Exception error)
        {
            FaultCalls++;
            Failure = error;
            ProductionFinished.TrySetResult();
            _completion.TrySetException(error);
        }
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            ProductionFinished.TrySetResult();
            _completion.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LifetimeNode(Exception? startupFailure = null, bool throwOnFault = false,
        Exception? disposeFailure = null) : IFlowSource
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Faulted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Completion => _completion.Task;
        public int StartCalls { get; private set; }
        public int FaultCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public Exception? Failure { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCalls++;
            return startupFailure is null ? Task.CompletedTask : Task.FromException(startupFailure);
        }
        public void Complete() => _completion.TrySetResult();
        public void Fail(Exception error) => _completion.TrySetException(error);
        public void Fault(Exception error)
        {
            FaultCalls++;
            Failure = error;
            _completion.TrySetException(error);
            Faulted.TrySetResult();
            if (throwOnFault) throw new InvalidOperationException("fault callback failed");
        }
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            Complete();
            return disposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(disposeFailure);
        }
    }
}
