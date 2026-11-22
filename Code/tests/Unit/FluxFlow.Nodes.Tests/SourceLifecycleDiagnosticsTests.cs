using System.Threading.Tasks.Dataflow;
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
        var actual = await Should.ThrowAsync<InvalidOperationException>(() => source.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
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
        if (stop)
            source.Complete();
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
            if (faultFirst)
                source.Fault(source.Original);
            else
                source.Complete();
            await source.Cancelled.Task.WaitAsync(timeout);
        }
        finally
        {
            source.Release.TrySetResult();
        }

        var actual = await Should.ThrowAsync<Exception>(() => source.Completion.WaitAsync(timeout));
        var errors = actual is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToArray() : new[]
        {
            actual
        };
        errors.ShouldBe(faultFirst ? new[] { source.Original, source.Callback } : new[] { source.Callback });
        var stop = observer.Values.Single(x => x.Operation == "source.stop");
        stop.Exception.ShouldBeOfType<AggregateException>().Flatten().InnerExceptions.ShouldHaveSingleItem().ShouldBeSameAs(source.Callback);
        source.Callback.StackTrace.ShouldNotBeNullOrWhiteSpace();
        observer.Values.Count.ShouldBe(faultFirst ? 2 : 1);
        if (faultFirst)
            observer.Values.Single(x => x.Operation == "source").Exception.ShouldBeSameAs(source.Original);
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
        if (faultFirst)
            source.Fault(source.Original);
        else
            await source.StartAsync();
        var actual = await Should.ThrowAsync<InvalidOperationException>(() => source.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        actual.ShouldBeSameAs(source.Disposal);
        var report = observer.Values.Single(x => x.Operation == "source.dispose");
        report.Exception.ShouldBeSameAs(source.Disposal);
        report.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        report.Exception.InnerException.ShouldBeOfType<ArgumentException>();
        if (faultFirst)
            (await Should.ThrowAsync<InvalidOperationException>(() => source.Completion)).ShouldBeSameAs(source.Original);
        else
            source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        source.Output.Completion.IsCompleted.ShouldBeTrue();
        await source.DisposeAsync();
        source.DisposalCalls.ShouldBe(1);
        observer.Values.Count.ShouldBe(faultFirst ? 2 : 1);
        observer.Activity.ShouldBeNull();
        Activity.Current.ShouldBeSameAs(host);
        host.Status.ShouldBe(ActivityStatusCode.Unset);
    }

    private sealed class ShutdownSource : FlowNode, IFlowSource
    {
        public Task ProductionCompletion => Lifetime.ProductionCompletion;

        public void StopProduction() => Lifetime.StopProduction();
        public Task StartAsync(CancellationToken cancellationToken = default) => Lifetime.StartAsync(cancellationToken);
        public ShutdownSource()
        {
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, produce: RunAsync);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }

        public Exception Original { get; } = new InvalidOperationException("production");
        public Exception Callback { get; } = new InvalidOperationException("cancellation");
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() =>
            {
                Cancelled.TrySetResult();
                throw Callback;
            });
            Entered.TrySetResult();
            await Release.Task;
        }
    }

    private sealed class DisposalSource : FlowNode, IFlowSource
    {
        public Task ProductionCompletion => Lifetime.ProductionCompletion;

        public void StopProduction() => Lifetime.StopProduction();
        public Task StartAsync(CancellationToken cancellationToken = default) => Lifetime.StartAsync(cancellationToken);
        public DisposalSource()
        {
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, produce: RunAsync);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }

        public Exception Original { get; } = new InvalidOperationException("production");
        public Exception Disposal { get; } = new InvalidOperationException("disposal", new ArgumentException("inner"));
        public int DisposalCalls { get; private set; }

        private Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        protected override ValueTask OnDisposeAsync()
        {
            DisposalCalls++;
            throw Disposal;
        }
    }

    [Fact]
    public async Task Processing_node_completion_and_disposal_faults_are_distinct_lifecycle_reports()
    {
        using var observer = new Observer();
        var completion = new InvalidOperationException("flush");
        var disposal = new InvalidOperationException("release");
        var node = new FailingNode(completion, disposal);
        node.Complete();
        (await Should.ThrowAsync<InvalidOperationException>(() => node.Completion.WaitAsync(TimeSpan.FromSeconds(5)))).ShouldBeSameAs(completion);
        (await Should.ThrowAsync<InvalidOperationException>(() => node.DisposeAsync().AsTask())).ShouldBeSameAs(disposal);
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
        Should.Throw<ObjectDisposedException>(() =>
        {
            _ = node.Token;
        });
        await node.DisposeAsync();
        observer.Values.Count.ShouldBe(1);
    }

    private sealed class CancellationNode : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }
        public Exception Failure { get; } = new InvalidOperationException("stop callback");
        public CancellationToken Token => Stopping;

        public CancellationNode()
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Stopping.Register(() => throw Failure);
            Lifetime.Observe();
        }

        private Task ProcessAsync(FlowMessage message) => Task.CompletedTask;
    }

    private sealed class FailingNode : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public FailingNode(Exception completion, Exception disposal)
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, flush: OnInputCompletedAsync, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            this.completion = completion;
            this.disposal = disposal;
            Lifetime.Observe();
        }

        private readonly Exception disposal;
        private readonly Exception completion;
        private Task ProcessAsync(FlowMessage message) => Task.CompletedTask;
        private ValueTask OnInputCompletedAsync() => throw completion;
        protected override ValueTask OnDisposeAsync() => throw disposal;
    }

    private sealed class Probe : FlowNode, IFlowSource
    {
        public Task ProductionCompletion => Lifetime.ProductionCompletion;

        public void StopProduction() => Lifetime.StopProduction();
        public Task StartAsync(CancellationToken cancellationToken = default) => Lifetime.StartAsync(cancellationToken);
        public Probe(Exception exception)
        {
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, produce: RunAsync);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            this.exception = exception;
            Lifetime.Observe();
        }

        private readonly Exception exception;
        private Task RunAsync(CancellationToken cancellationToken) => throw exception;
    }

    private sealed class NormalSource : FlowNode, IFlowSource
    {
        public Task ProductionCompletion => Lifetime.ProductionCompletion;

        public void StopProduction() => Lifetime.StopProduction();
        public Task StartAsync(CancellationToken cancellationToken = default) => Lifetime.StartAsync(cancellationToken);
        public NormalSource(bool wait)
        {
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, produce: RunAsync);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            this.wait = wait;
            Lifetime.Observe();
        }

        private readonly bool wait;
        private Task RunAsync(CancellationToken cancellationToken) => wait ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken) : Task.CompletedTask;
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

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void Dispose()
        {
            _discovery.Dispose();
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
        }
    }
}
