using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class FlowLifetimeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Startup_failure_faults_all_components_before_later_sources_start(int failingIndex)
    {
        var failure = new InvalidOperationException("source startup failed");
        var sources = Enumerable.Range(0, 3).Select(index =>
            new LifetimeSource(index == failingIndex ? failure : null, throwOnFault: index == 1)).ToArray();
        var lifetime = new FlowLifetime(sources, sources);
        var completion = lifetime.WaitForCompletionAsync([]);

        (await Record.ExceptionAsync(() => lifetime.StartAsync(default).WaitAsync(Deadline))).ShouldBeSameAs(failure);
        sources.Select(source => source.StartCalls)
            .ShouldBe(Enumerable.Range(0, 3).Select(index => index <= failingIndex ? 1 : 0));
        sources.ShouldAllBe(source => source.FaultCalls == 1 && ReferenceEquals(source.FaultError, failure));
        sources.ShouldAllBe(source => source.Completion.IsFaulted && source.DisposeCalls == 0);
        (await Record.ExceptionAsync(() => completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        lifetime.Stop();
        sources.ShouldAllBe(source => source.CompleteCalls == 0);
        lifetime.Failure.ShouldBeSameAs(failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Component_failure_faults_siblings_but_waits_for_combined_completion(bool additionalFailure)
    {
        var failure = new InvalidOperationException("component execution failed");
        var nodes = new[] { new LifetimeNode(), new LifetimeNode(throwOnFault: true), new LifetimeNode() };
        var originWork = Gate();
        var acceptedWork = Gate();
        var lifetime = new FlowLifetime(nodes, nodes);
        var completion = lifetime.WaitForCompletionAsync([originWork.Task, acceptedWork.Task]);
        try
        {
            await lifetime.StartAsync(default);
            if (additionalFailure) originWork.TrySetException(failure);
            else nodes[0].Fail(failure);
            await nodes[2].Faulted.Task.WaitAsync(Deadline);
            nodes.ShouldAllBe(node => node.FaultCalls == 1 && ReferenceEquals(node.FaultError, failure));
            nodes.ShouldAllBe(node => node.Completion.IsFaulted && node.DisposeCalls == 0);
            completion.IsCompleted.ShouldBeFalse();
            originWork.TrySetResult();
            completion.IsCompleted.ShouldBeFalse();
            acceptedWork.TrySetException(new ArgumentException("later accepted work failed"));
            (await Record.ExceptionAsync(() => completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        }
        finally
        {
            originWork.TrySetResult();
            acceptedWork.TrySetResult();
            foreach (var node in nodes) node.Finish();
            await Record.ExceptionAsync(() => completion.WaitAsync(Deadline));
        }
    }

    [Fact]
    public async Task Sources_do_not_start_after_failure_during_an_earlier_start()
    {
        var failure = new InvalidOperationException("component failed during source startup");
        var release = Gate();
        var first = new LifetimeSource(startupGate: release.Task);
        var later = new LifetimeSource();
        var origin = new LifetimeNode();
        var lifetime = new FlowLifetime([first, later, origin], [first, later, origin]);
        var completion = lifetime.WaitForCompletionAsync([]);
        var starting = lifetime.StartAsync(default);
        try
        {
            first.StartCalls.ShouldBe(1);
            origin.Fail(failure);
            await origin.Faulted.Task.WaitAsync(Deadline);
            starting.IsCompleted.ShouldBeFalse();
            later.StartCalls.ShouldBe(0);
            release.TrySetResult();
            (await Record.ExceptionAsync(() => starting.WaitAsync(Deadline))).ShouldBeSameAs(failure);
            later.StartCalls.ShouldBe(0);
            first.FaultError.ShouldBeSameAs(failure);
            later.FaultError.ShouldBeSameAs(failure);
            (await Record.ExceptionAsync(() => completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        }
        finally
        {
            release.TrySetResult();
            first.Finish();
            later.Finish();
            origin.Finish();
            await Record.ExceptionAsync(() => starting.WaitAsync(Deadline));
            await Record.ExceptionAsync(() => completion.WaitAsync(Deadline));
        }
    }

    [Fact]
    public async Task Canceled_startup_does_not_start_sources_and_settles_completion()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var sources = new[] { new LifetimeSource(), new LifetimeSource() };
        var lifetime = new FlowLifetime(sources, sources);
        var completion = lifetime.WaitForCompletionAsync([]);
        var failure = await Record.ExceptionAsync(() => lifetime.StartAsync(cancellation.Token).WaitAsync(Deadline));
        Assert.IsAssignableFrom<OperationCanceledException>(failure).CancellationToken.ShouldBe(cancellation.Token);
        sources.ShouldAllBe(source => source.StartCalls == 0 && source.FaultCalls == 1 && source.CompleteCalls == 0);
        sources.ShouldAllBe(source => ReferenceEquals(source.FaultError, failure));
        (await Record.ExceptionAsync(() => completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        completion.IsCanceled.ShouldBeTrue();
    }

    [Fact]
    public async Task Graceful_stop_stops_controlled_sources_once()
    {
        var entry = new LifetimeSource();
        var controlled = new LifetimeSource();
        var downstream = new LifetimeNode();
        var nodes = new LifetimeNode[] { entry, controlled, downstream };
        var lifetime = new FlowLifetime(nodes, [entry]);
        var completion = lifetime.WaitForCompletionAsync([]);
        try
        {
            await lifetime.StartAsync(default);
            entry.StartCalls.ShouldBe(1);
            controlled.StartCalls.ShouldBe(1);
            lifetime.Stop();
            lifetime.Stop();
            entry.CompleteCalls.ShouldBe(1);
            controlled.CompleteCalls.ShouldBe(1);
            downstream.CompleteCalls.ShouldBe(0);
            completion.IsCompleted.ShouldBeFalse();
            downstream.Completion.IsCompleted.ShouldBeFalse();
            downstream.Finish();
            await completion.WaitAsync(Deadline);
            completion.IsCompletedSuccessfully.ShouldBeTrue();
            nodes.ShouldAllBe(node => node.FaultCalls == 0 && node.DisposeCalls == 0);
        }
        finally
        {
            foreach (var node in nodes) node.Finish();
            await completion.WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task Stop_failure_faults_siblings_without_replacing_the_initiating_error()
    {
        var failure = new InvalidOperationException("component completion failed");
        var failing = new LifetimeNode(completeFailure: failure, throwOnFault: true);
        var sibling = new LifetimeNode();
        var lifetime = new FlowLifetime([failing, sibling], [failing, sibling]);
        var completion = lifetime.WaitForCompletionAsync([]);
        Should.Throw<InvalidOperationException>(lifetime.Stop).ShouldBeSameAs(failure);
        failing.CompleteCalls.ShouldBe(1);
        sibling.FaultCalls.ShouldBe(1);
        sibling.FaultError.ShouldBeSameAs(failure);
        failing.FaultError.ShouldBeSameAs(failure);
        (await Record.ExceptionAsync(() => completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        lifetime.Failure.ShouldBeSameAs(failure);
    }

    private class LifetimeNode(Exception? completeFailure = null, bool throwOnFault = false) : IFlowNode
    {
        private readonly TaskCompletionSource _completion = Gate();
        public TaskCompletionSource Faulted { get; } = Gate();
        public Task Completion => _completion.Task;
        public int CompleteCalls { get; private set; }
        public int FaultCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public Exception? FaultError { get; private set; }
        public void Complete()
        {
            CompleteCalls++;
            if (completeFailure is not null) throw completeFailure;
            Finish();
        }
        public void Fault(Exception error)
        {
            FaultCalls++;
            FaultError = error;
            _completion.TrySetException(error);
            Faulted.TrySetResult();
            if (throwOnFault) throw new InvalidOperationException("fault callback failed");
        }
        public void Fail(Exception error) => _completion.TrySetException(error);
        public void Finish() => _completion.TrySetResult();
        public ValueTask DisposeAsync() { DisposeCalls++; Finish(); return ValueTask.CompletedTask; }
    }

    private sealed class LifetimeSource(Exception? startupFailure = null, bool throwOnFault = false, Task? startupGate = null)
        : LifetimeNode(throwOnFault: throwOnFault), IFlowSource
    {
        public int StartCalls { get; private set; }
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCalls++;
            if (startupGate is not null) await startupGate.WaitAsync(cancellationToken);
            if (startupFailure is not null) throw startupFailure;
        }
    }
}
