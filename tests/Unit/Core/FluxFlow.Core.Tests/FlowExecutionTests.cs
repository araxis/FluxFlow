using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class FlowExecutionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Failed_startup_stops_all_components_and_releases_only_owned_instances()
    {
        var failure = new InvalidOperationException("startup failed");
        var first = new LifetimeProbe();
        var failing = new LifetimeProbe(failure);
        var borrowed = new LifetimeProbe();
        var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        workflow.Add("first", first);
        workflow.Add("failing", failing);
        workflow.Add("borrowed", borrowed, ComponentOwnership.Borrowed);
        (await Should.ThrowAsync<InvalidOperationException>(() => app.StartAsync().WaitAsync(Deadline)))
            .ShouldBeSameAs(failure);
        first.StartCalls.ShouldBe(1);
        failing.StartCalls.ShouldBe(1);
        borrowed.StartCalls.ShouldBe(0);
        first.DisposeCalls.ShouldBe(1);
        failing.DisposeCalls.ShouldBe(1);
        borrowed.DisposeCalls.ShouldBe(0);
        borrowed.Completion.IsFaulted.ShouldBeTrue();
        await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask());
        first.DisposeCalls.ShouldBe(1);
        await borrowed.DisposeAsync();
    }

    [Fact]
    public async Task Fault_waits_for_active_processing_before_disposing_owned_resources()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var component = new ActiveNode(release.Task);
        var app = new FlowApplication();
        var handle = app.AddWorkflow("main").Add("active", component);
        handle.Output.DiscardUnmatched();
        await app.StartAsync();
        (await app.SendAsync(handle.Input, FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
        await component.Entered.Task.WaitAsync(Deadline);
        var failure = new InvalidOperationException("forced failure");
        app.Fault(failure);
        var disposing = app.DisposeAsync().AsTask();
        try
        {
            disposing.IsCompleted.ShouldBeFalse();
            component.ResourceDisposed.ShouldBeFalse();
            component.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        await Should.ThrowAsync<AggregateException>(() => disposing.WaitAsync(Deadline));
        component.ResourceDisposed.ShouldBeTrue();
        component.ProcessingExited.ShouldBeTrue();
    }

    [Fact]
    public async Task Fault_waits_for_a_running_source_before_disposing_its_resources()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new ActiveSource(release.Task);
        var app = new FlowApplication();
        app.AddWorkflow("main").Add("active", source).Output.DiscardUnmatched();
        await app.StartAsync();
        await source.Entered.Task.WaitAsync(Deadline);
        app.Fault(new InvalidOperationException("forced failure"));
        var disposing = app.DisposeAsync().AsTask();
        try
        {
            disposing.IsCompleted.ShouldBeFalse();
            source.ResourceDisposed.ShouldBeFalse();
            source.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        await Should.ThrowAsync<AggregateException>(() => disposing.WaitAsync(Deadline));
        source.ResourceDisposed.ShouldBeTrue();
        source.ProducingExited.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_failure_still_completes_owned_and_borrowed_components(bool started)
    {
        var failure = new InvalidOperationException("Timer stop failed.");
        var clock = new ApplicationCleanupTimeProvider { StopError = failure };
        var owned = new LifetimeProbe();
        var borrowed = new LifetimeProbe();
        var app = new FlowApplication(timeProvider: clock);
        var workflow = app.AddWorkflow("main");
        workflow.Add("owned", owned);
        workflow.Add("borrowed", borrowed, ComponentOwnership.Borrowed);
        if (started) await app.StartAsync();

        (await Should.ThrowAsync<InvalidOperationException>(() => app.StopAsync().WaitAsync(Deadline))).ShouldBeSameAs(failure);

        await Task.WhenAll(owned.Completion, borrowed.Completion).WaitAsync(Deadline);
        if (started) await app.Completion.WaitAsync(Deadline);
        owned.DisposeCalls.ShouldBe(0);
        borrowed.DisposeCalls.ShouldBe(0);
        var disposing = app.DisposeAsync().AsTask();
        var error = await Should.ThrowAsync<AggregateException>(() => disposing.WaitAsync(Deadline));
        error.Flatten().InnerExceptions.ShouldContain(failure);
        app.DisposeAsync().AsTask().ShouldBeSameAs(disposing);
        owned.DisposeCalls.ShouldBe(1);
        borrowed.DisposeCalls.ShouldBe(0);
        clock.DisposeCalls.ShouldBe(1);
        await borrowed.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_preserves_component_and_timer_cleanup_errors()
    {
        var componentError = new InvalidOperationException("Component disposal failed.");
        var timerError = new InvalidOperationException("Timer disposal failed.");
        var clock = new ApplicationCleanupTimeProvider { DisposeError = timerError };
        var first = new LifetimeProbe();
        var failing = new LifetimeProbe(disposalFailure: componentError);
        var borrowed = new LifetimeProbe();
        var app = new FlowApplication(timeProvider: clock);
        var workflow = app.AddWorkflow("main");
        workflow.Add("first", first);
        workflow.Add("failing", failing);
        workflow.Add("borrowed", borrowed, ComponentOwnership.Borrowed);
        await app.StartAsync();

        var disposing = app.DisposeAsync().AsTask();
        var error = await Should.ThrowAsync<AggregateException>(() => disposing.WaitAsync(Deadline));

        error.Flatten().InnerExceptions.ShouldBe([componentError, timerError]);
        first.DisposeCalls.ShouldBe(1);
        failing.DisposeCalls.ShouldBe(1);
        borrowed.DisposeCalls.ShouldBe(0);
        borrowed.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        clock.DisposeCalls.ShouldBe(1);
        app.DisposeAsync().AsTask().ShouldBeSameAs(disposing);
        await borrowed.DisposeAsync();
    }

    private sealed class LifetimeProbe(Exception? startupFailure = null, Exception? disposalFailure = null) : IFlowComponent, IFlowSource
    {
        private readonly ActionBlock<FlowMessage> _input = new(_ => { });
        public int StartCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public FlowPorts Ports { get; } = new();
        public Task Completion => _input.Completion;
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCalls++;
            return startupFailure is null ? Task.CompletedTask : Task.FromException(startupFailure);
        }
        public void Complete() => _input.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)_input).Fault(error);
        public async ValueTask DisposeAsync()
        {
            DisposeCalls++;
            Complete();
            try { await Completion; }
            catch { }
            if (disposalFailure is not null) throw disposalFailure;
        }
    }

    private sealed class ActiveNode(Task release) : FlowNode
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ProcessingExited { get; private set; }
        public bool ResourceDisposed { get; private set; }
        protected override async Task ProcessAsync(FlowMessage message)
        {
            Entered.TrySetResult();
            await release;
            ProcessingExited = true;
        }
        protected override ValueTask OnDisposeAsync() { ResourceDisposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class ActiveSource(Task release) : FlowSource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ProducingExited { get; private set; }
        public bool ResourceDisposed { get; private set; }
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await release;
            ProducingExited = true;
        }
        protected override ValueTask OnDisposeAsync() { ResourceDisposed = true; return ValueTask.CompletedTask; }
    }
}
