using FluxFlow.Core.Application;
using FluxFlow.Core.Execution;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class FlowExecutionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task The_same_instance_cannot_be_registered_under_another_name_workflow_or_application()
    {
        var component = new LifetimeProbe();
        var services = new ServiceCollection();
        foreach (var address in new[] { "first.original", "first.alias", "second.alias" })
            services.AddComponentFactory(address, _ => component);
        await using var provider = services.BuildServiceProvider();
        var otherServices = new ServiceCollection();
        otherServices.AddComponentFactory("external.alias", _ => component);
        await using var otherProvider = otherServices.BuildServiceProvider();
        provider.GetRequiredKeyedService<IFlowNode>("first.original").ShouldBeSameAs(component);
        Should.Throw<ArgumentException>(() => provider.GetRequiredKeyedService<IFlowNode>("first.alias"));
        Should.Throw<ArgumentException>(() => provider.GetRequiredKeyedService<IFlowNode>("second.alias"));
        Should.Throw<ArgumentException>(() => otherProvider.GetRequiredKeyedService<IFlowNode>("external.alias"));
        component.DisposeCalls.ShouldBe(0);
        await otherProvider.DisposeAsync();
        component.DisposeCalls.ShouldBe(0);
        await provider.DisposeAsync();
        component.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Application_stops_components_and_native_DI_disposes_them_once()
    {
        var component = new LifetimeProbe();
        var services = new ServiceCollection();
        services.AddComponentFactory("main.source", _ => component);
        await using var provider = services.BuildServiceProvider();
        await using var execution = await provider.BuildApplicationAsync();
        await execution.DisposeAsync();
        await execution.DisposeAsync();
        component.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        component.DisposeCalls.ShouldBe(0);
        await provider.DisposeAsync();
        await provider.DisposeAsync();
        component.DisposeCalls.ShouldBe(1);
        Should.Throw<InvalidOperationException>(() => execution.StartAsync());
    }

    [Fact]
    public async Task Failed_startup_stops_unstarted_components_and_provider_owns_disposal()
    {
        var failure = new InvalidOperationException("startup failed");
        var first = new LifetimeProbe();
        var failing = new LifetimeProbe(failure);
        var unstarted = new LifetimeProbe();
        var services = new ServiceCollection();
        services.AddComponentFactory("main.first", _ => first)
            .AddComponentFactory("main.failing", _ => failing)
            .AddComponentFactory("main.unstarted", _ => unstarted);
        await using var provider = services.BuildServiceProvider();
        var appExecution = await provider.BuildApplicationAsync();
        (await Should.ThrowAsync<InvalidOperationException>(() => appExecution.StartAsync().WaitAsync(Deadline)))
            .ShouldBeSameAs(failure);
        first.StartCalls.ShouldBe(1);
        failing.StartCalls.ShouldBe(1);
        unstarted.StartCalls.ShouldBe(0);
        first.DisposeCalls.ShouldBe(0);
        failing.DisposeCalls.ShouldBe(0);
        unstarted.DisposeCalls.ShouldBe(0);
        unstarted.Completion.IsFaulted.ShouldBeTrue();
        await Should.ThrowAsync<AggregateException>(() => appExecution.DisposeAsync().AsTask());
        (await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask())).Flatten().InnerExceptions.ShouldContain(failure);
        first.DisposeCalls.ShouldBe(1);
        failing.DisposeCalls.ShouldBe(1);
        unstarted.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Start_requested_again_by_a_source_shares_the_original_start()
    {
        var services = new ServiceCollection();
        var node = new CallbackNode();
        services.AddComponentFactory("main.node", _ => node);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        Task? nested = null;
        var completedEarly = false;
        node.OnStart = () =>
        {
            nested = app.StartAsync();
            completedEarly = nested.IsCompleted;
        };
        var starting = app.StartAsync();
        await starting.WaitAsync(Deadline);
        completedEarly.ShouldBeFalse();
        nested.ShouldBeSameAs(starting);
        node.StartCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Stop_requested_again_by_an_unstarted_component_does_not_repeat_completion()
    {
        var services = new ServiceCollection();
        var node = new CallbackNode();
        services.AddComponentFactory("main.node", _ => node);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        Task? nested = null;
        node.OnComplete = () => nested = app.StopAsync();
        await app.StopAsync().WaitAsync(Deadline);
        await nested!.WaitAsync(Deadline);
        node.CompleteCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_callers_share_cleanup_even_when_a_component_calls_back(bool fail)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = fail ? new InvalidOperationException("cleanup failed") : null;
        var services = new ServiceCollection();
        var node = new CallbackNode(release.Task, failure);
        services.AddComponentFactory("main.node", _ => node);
        await using var provider = services.BuildServiceProvider();
        var app = await provider.BuildApplicationAsync();
        Task? nested = null;
        node.OnComplete = () => nested = app.DisposeAsync().AsTask();
        var first = app.DisposeAsync().AsTask();
        var second = app.DisposeAsync().AsTask();
        var completedEarly = first.IsCompleted || second.IsCompleted || nested!.IsCompleted;
        release.TrySetResult();
        foreach (var task in new[] { first, second, nested! })
        {
            if (failure is null) await task.WaitAsync(Deadline);
            else (await Should.ThrowAsync<AggregateException>(() => task.WaitAsync(Deadline)))
                .Flatten().InnerExceptions.ShouldContain(failure);
        }
        completedEarly.ShouldBeFalse();
        second.ShouldBeSameAs(first);
        nested.ShouldBeSameAs(first);
        node.DisposeCalls.ShouldBe(0);
        if (failure is null) await provider.DisposeAsync();
        else (await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask())).Flatten().InnerExceptions.ShouldContain(failure);
        node.DisposeCalls.ShouldBe(1);
    }

    private sealed class CallbackNode(Task? completionRelease = null, Exception? completionFailure = null) : IFlowSource
    {
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action? OnStart { get; set; }
        public Action? OnComplete { get; set; }
        public int CompleteCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public int StartCalls { get; private set; }
        public Task Completion => _completed.Task;
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (++StartCalls == 1) OnStart?.Invoke();
            return Task.CompletedTask;
        }
        public void Complete()
        {
            if (++CompleteCalls != 1) return;
            OnComplete?.Invoke();
            _ = FinishAsync();
        }
        private async Task FinishAsync()
        {
            if (completionRelease is not null) await completionRelease;
            if (completionFailure is not null) _completed.TrySetException(completionFailure);
            else _completed.TrySetResult();
        }
        public void Fault(Exception error) => _completed.TrySetException(error);
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task A_precanceled_start_leaves_the_application_available_for_composition_and_start()
    {
        var services = new ServiceCollection();
        services.AddComponentFactory("main.source", _ => new LifetimeProbe());
        await using var provider = services.BuildServiceProvider();
        var app = provider.GetRequiredService<FlowApplication>();
        Should.Throw<OperationCanceledException>(() => app.StartAsync(new CancellationToken(canceled: true)));
        await provider.BuildApplicationAsync();
        var node = (LifetimeProbe)provider.GetRequiredKeyedService<IFlowNode>("main.source");
        await app.StartAsync();
        node.StartCalls.ShouldBe(1);
        await app.StopAsync();
    }

    [Fact]
    public async Task Fault_before_start_closes_composition_start_and_admission()
    {
        var services = new ServiceCollection();
        services.AddComponentFactory("main.node", _ => new DrainEcho())
            .AddInputPort<DrainEcho>("main.node", "Input", node => node.Input)
            .AddOutputPort<DrainEcho>("main.node", "Output", node => node.Output);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        app.Fault(new InvalidOperationException("closed before startup"));
        Should.Throw<InvalidOperationException>(() => provider.Link("main.node.Output", "main.node.Input"));
        Should.Throw<InvalidOperationException>(() => app.StartAsync());
    }

    [Fact]
    public async Task Native_DI_cleanup_stops_at_the_first_failure_without_repeating_disposal()
    {
        var firstFailure = new InvalidOperationException("first cleanup");
        var secondFailure = new InvalidOperationException("second cleanup");
        var first = new LifetimeProbe(disposalFailure: firstFailure);
        var second = new LifetimeProbe(disposalFailure: secondFailure);
        var services = new ServiceCollection();
        services.AddComponentFactory("main.first", _ => first).AddComponentFactory("main.second", _ => second);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.DisposeAsync();
        (await Should.ThrowAsync<InvalidOperationException>(() => provider.DisposeAsync().AsTask())).ShouldBeSameAs(secondFailure);
        await provider.DisposeAsync();
        first.DisposeCalls.ShouldBe(0);
        second.DisposeCalls.ShouldBe(1);
        Should.Throw<InvalidOperationException>(() => app.StartAsync());
    }

    [Fact]
    public async Task Fault_waits_for_active_processing_before_disposing_owned_resources()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var component = new ActiveNode(release.Task);
        var services = new ServiceCollection();
        services.AddComponentFactory("main.active", _ => component)
            .AddInputPort<ActiveNode>("main.active", "Input", node => node.Input)
            .AddOutputPort<ActiveNode>("main.active", "Output", node => node.Output);
        TestSink.Register(services, "main.active.Output");
        await using var provider = services.BuildServiceProvider();
        var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        (await component.Input.SendAsync(FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(1))))).ShouldBeTrue();
        await component.Entered.Task.WaitAsync(Deadline);
        var failure = new InvalidOperationException("forced failure");
        appExecution.Fault(failure);
        var disposing = appExecution.DisposeAsync().AsTask();
        try
        {
            disposing.IsCompleted.ShouldBeFalse();
            component.ResourceDisposed.ShouldBeFalse();
            component.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        await Should.ThrowAsync<AggregateException>(() => disposing.WaitAsync(Deadline));
        component.ResourceDisposed.ShouldBeFalse();
        await Should.ThrowAsync<Exception>(() => provider.DisposeAsync().AsTask());
        component.ResourceDisposed.ShouldBeTrue();
        component.ProcessingExited.ShouldBeTrue();
    }

    [Fact]
    public async Task Fault_waits_for_a_running_source_before_disposing_its_resources()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new ActiveSource(release.Task);
        var services = new ServiceCollection();
        services.AddComponentFactory("main.active", _ => source)
            .AddOutputPort<ActiveSource>("main.active", "Output", node => node.Output);
        TestSink.Register(services, "main.active.Output");
        await using var provider = services.BuildServiceProvider();
        var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        await source.Entered.Task.WaitAsync(Deadline);
        appExecution.Fault(new InvalidOperationException("forced failure"));
        var disposing = appExecution.DisposeAsync().AsTask();
        try
        {
            disposing.IsCompleted.ShouldBeFalse();
            source.ResourceDisposed.ShouldBeFalse();
            source.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        await Should.ThrowAsync<AggregateException>(() => disposing.WaitAsync(Deadline));
        source.ResourceDisposed.ShouldBeFalse();
        await Should.ThrowAsync<Exception>(() => provider.DisposeAsync().AsTask());
        source.ResourceDisposed.ShouldBeTrue();
        source.ProducingExited.ShouldBeTrue();
    }

    [Fact]
    public async Task Fan_in_keeps_target_open_until_the_last_producer_drains_during_stop()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        // Register the receiver first to ensure completion does not depend on registration order.
        var sink = new DrainSink();
        var fast = new DrainEcho();
        var slow = new DrainEcho(release.Task, entered);
        services.AddComponentFactory("receiver.sink", _ => sink).AddInputPort<DrainSink>("receiver.sink", "Input", node => node.Input);
        foreach (var (name, node) in new[] { ("fast", fast), ("slow", slow) })
            services.AddComponentFactory("senders." + name, _ => node)
                .AddInputPort<DrainEcho>("senders." + name, "Input", value => value.Input)
                .AddOutputPort<DrainEcho>("senders." + name, "Output", value => value.Output);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("senders.fast.Output", "receiver.sink.Input");
            ports.Link("senders.slow.Output", "receiver.sink.Input");
        });
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var first = FlowMessage.Create(JsonFlowValue.Null);
        var second = FlowMessage.Create(JsonFlowValue.Null);
        Task stopping;
        try
        {
            (await fast.Input.SendAsync(first)).ShouldBeTrue();
            (await slow.Input.SendAsync(second)).ShouldBeTrue();
            await entered.Task.WaitAsync(Deadline);
            await sink.FirstReceived.Task.WaitAsync(Deadline);
            fast.Complete();
            await fast.Completion.WaitAsync(Deadline);
            sink.Completion.IsCompleted.ShouldBeFalse();
            stopping = app.StopAsync();
            stopping.IsCompleted.ShouldBeFalse();
            sink.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        await stopping.WaitAsync(Deadline);
        sink.Received.ShouldBe([first, second]);
        sink.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    private sealed class DrainEcho(Task? release = null, TaskCompletionSource? entered = null) : FlowTransformNode
    {
        protected override async Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
        {
            entered?.TrySetResult();
            if (release is not null) await release;
            return [message];
        }
    }

    private sealed class DrainSink : FlowNode
    {
        public List<FlowMessage> Received { get; } = [];
        public TaskCompletionSource FirstReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task ProcessAsync(FlowMessage message)
        {
            Received.Add(message);
            FirstReceived.TrySetResult();
            return Task.CompletedTask;
        }
    }



    private sealed class LifetimeProbe(Exception? startupFailure = null, Exception? disposalFailure = null) : IFlowSource
    {
        private readonly ActionBlock<FlowMessage> _input = new(_ => { });
        public int StartCalls { get; private set; }
        public int DisposeCalls { get; private set; }
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
