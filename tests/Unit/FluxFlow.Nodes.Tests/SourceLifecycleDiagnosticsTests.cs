using System.Diagnostics;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

[CollectionDefinition("Source lifecycle diagnostics", DisableParallelization = true)]
public sealed class SourceLifecycleDiagnosticsCollection;

[Collection("Source lifecycle diagnostics")]
public sealed class SourceLifecycleDiagnosticsTests
{
    [Fact]
    public async Task Throwing_observer_cannot_replace_source_fault_or_mutate_host_activity()
    {
        using var observer = new Observer();
        using var host = new Activity("host").Start();
        var exception = new InvalidOperationException("source failed", new ArgumentException("inner"));
        await using var source = new Probe(exception);
        await source.StartAsync();
        var actual = await Should.ThrowAsync<InvalidOperationException>(
            () => source.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        actual.ShouldBeSameAs(exception);
        source.Fault(exception);
        var failure = observer.Values.ShouldHaveSingleItem();
        failure.Exception.ShouldBeSameAs(exception);
        failure.Exception.InnerException.ShouldBeOfType<ArgumentException>();
        failure.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        failure.ComponentType.ShouldBe(source.GetType().FullName);
        failure.Operation.ShouldBe("source");
        observer.Activity.ShouldBeNull();
        Activity.Current.ShouldBeSameAs(host);
        host.Status.ShouldBe(ActivityStatusCode.Unset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Successful_or_stopped_source_does_not_report_failure(bool stop)
    {
        using var observer = new Observer();
        await using var source = new NormalSource(stop);
        await source.StartAsync();
        if (stop) source.Complete();
        await source.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        observer.Values.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_callback_failure_is_reported_separately_from_original_fault(bool faultFirst)
    {
        using var observer = new Observer();
        using var host = new Activity("host").Start();
        var source = new ShutdownSource();
        var timeout = TimeSpan.FromSeconds(5);
        try
        {
            await source.StartAsync();
            await source.Entered.Task.WaitAsync(timeout);
            if (faultFirst) source.Fault(source.Original);
            else source.Complete();
            await source.Cancelled.Task.WaitAsync(timeout);
        }
        finally { source.Release.TrySetResult(); }
        var actual = await Should.ThrowAsync<Exception>(() => source.Completion.WaitAsync(timeout));
        var errors = actual is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions.ToArray() : new[] { actual };
        errors.ShouldBe(faultFirst ? new[] { source.Original, source.Callback } : new[] { source.Callback });
        var stop = observer.Values.Single(x => x.Operation == "source.stop");
        stop.Exception.ShouldBeOfType<AggregateException>().Flatten().InnerExceptions
            .ShouldHaveSingleItem().ShouldBeSameAs(source.Callback);
        source.Callback.StackTrace.ShouldNotBeNullOrWhiteSpace();
        observer.Values.Count.ShouldBe(faultFirst ? 2 : 1);
        if (faultFirst) observer.Values.Single(x => x.Operation == "source").Exception.ShouldBeSameAs(source.Original);
        await source.DisposeAsync();
        await source.DisposeAsync();
        observer.Values.Count.ShouldBe(faultFirst ? 2 : 1);
        observer.Activity.ShouldBeNull();
        Activity.Current.ShouldBeSameAs(host);
        host.Status.ShouldBe(ActivityStatusCode.Unset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposal_hook_failure_is_preserved_without_relogging_completion(bool faultFirst)
    {
        using var observer = new Observer();
        using var host = new Activity("host").Start();
        var source = new DisposalSource();
        if (faultFirst) source.Fault(source.Original);
        else await source.StartAsync();
        var actual = await Should.ThrowAsync<InvalidOperationException>(
            () => source.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        actual.ShouldBeSameAs(source.Disposal);
        var report = observer.Values.Single(x => x.Operation == "source.dispose");
        report.Exception.ShouldBeSameAs(source.Disposal);
        report.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        report.Exception.InnerException.ShouldBeOfType<ArgumentException>();
        if (faultFirst)
            (await Should.ThrowAsync<InvalidOperationException>(() => source.Completion)).ShouldBeSameAs(source.Original);
        else source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        source.Output.Completion.IsCompleted.ShouldBeTrue();
        await source.DisposeAsync();
        source.DisposalCalls.ShouldBe(1);
        observer.Values.Count.ShouldBe(faultFirst ? 2 : 1);
        observer.Activity.ShouldBeNull();
        Activity.Current.ShouldBeSameAs(host);
        host.Status.ShouldBe(ActivityStatusCode.Unset);
    }

    private sealed class ShutdownSource : FlowSource
    {
        public Exception Original { get; } = new InvalidOperationException("production");
        public Exception Callback { get; } = new InvalidOperationException("cancellation");
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() => { Cancelled.TrySetResult(); throw Callback; });
            Entered.TrySetResult();
            await Release.Task;
        }
    }

    private sealed class DisposalSource : FlowSource
    {
        public Exception Original { get; } = new InvalidOperationException("production");
        public Exception Disposal { get; } = new InvalidOperationException("disposal", new ArgumentException("inner"));
        public int DisposalCalls { get; private set; }
        protected override Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        protected override ValueTask OnDisposeAsync() { DisposalCalls++; throw Disposal; }
    }

    [Fact]
    public async Task Processing_node_completion_and_disposal_faults_are_distinct_lifecycle_reports()
    {
        using var observer = new Observer();
        var completion = new InvalidOperationException("flush");
        var disposal = new InvalidOperationException("release");
        var node = new FailingNode(completion, disposal);
        node.Complete();
        (await Should.ThrowAsync<InvalidOperationException>(() => node.Completion.WaitAsync(TimeSpan.FromSeconds(5))))
            .ShouldBeSameAs(completion);
        (await Should.ThrowAsync<InvalidOperationException>(() => node.DisposeAsync().AsTask()))
            .ShouldBeSameAs(disposal);
        await node.DisposeAsync();
        observer.Values.Count.ShouldBe(2);
        observer.Values[0].Operation.ShouldBe("node");
        observer.Values[0].Exception.ShouldBeSameAs(completion);
        observer.Values[1].Operation.ShouldBe("node.dispose");
        observer.Values[1].Exception.ShouldBeSameAs(disposal);
        observer.Values.ShouldAllBe(value => value.ComponentType == typeof(FailingNode).FullName);
    }

    [Fact]
    public async Task Disposal_cancellation_failure_is_reported_without_changing_completed_task()
    {
        using var observer = new Observer();
        var node = new CancellationNode();
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var error = await Should.ThrowAsync<AggregateException>(() => node.DisposeAsync().AsTask());
        error.Flatten().InnerExceptions.ShouldHaveSingleItem().ShouldBeSameAs(node.Failure);
        var report = observer.Values.ShouldHaveSingleItem();
        report.Exception.ShouldBeSameAs(error);
        report.Operation.ShouldBe("node.stop");
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        await node.DisposeAsync();
        observer.Values.Count.ShouldBe(1);
    }

    private sealed class CancellationNode : FlowNode
    {
        public Exception Failure { get; } = new InvalidOperationException("stop callback");
        public CancellationNode() => Stopping.Register(() => throw Failure);
        protected override Task ProcessAsync(FlowMessage message) => Task.CompletedTask;
    }

    private sealed class FailingNode(Exception completion, Exception disposal) : FlowNode
    {
        protected override Task ProcessAsync(FlowMessage message) => Task.CompletedTask;
        protected override ValueTask OnInputCompletedAsync() => throw completion;
        protected override ValueTask OnDisposeAsync() => throw disposal;
    }

    private sealed class Probe(Exception exception) : FlowSource
    {
        protected override Task RunAsync(CancellationToken cancellationToken) => throw exception;
    }

    private sealed class NormalSource(bool wait) : FlowSource
    {
        protected override Task RunAsync(CancellationToken cancellationToken)
            => wait ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken) : Task.CompletedTask;
    }

    private sealed class Observer : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        public List<FlowLifecycleException> Values { get; } = [];
        public Activity? Activity { get; private set; }
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _discovery;
        public Observer() => _discovery = DiagnosticListener.AllListeners.Subscribe(this);
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == FlowDiagnostics.ListenerName)
                _subscriptions.Add(value.Subscribe(this, name => name == FlowDiagnostics.LifecycleExceptionEvent));
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            Values.Add((FlowLifecycleException)value.Value!);
            Activity = System.Diagnostics.Activity.Current;
            throw new InvalidOperationException("observer failed");
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose()
        {
            _discovery.Dispose();
            foreach (var subscription in _subscriptions) subscription.Dispose();
        }
    }
}
