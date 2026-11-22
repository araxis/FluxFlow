using FluxFlow.Application.Addressing;
using FluxFlow.Application;
using FluxFlow.Application.Execution;
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
        foreach (var address in new[]
        {
            "first.original",
            "first.alias",
            "second.alias"
        }

        )
            services.AddComponentFactory(ComponentAddress.Parse(address), _ => component);
        await using var provider = services.BuildServiceProvider();
        var otherServices = new ServiceCollection();
        otherServices.AddComponentFactory(ComponentAddress.Parse("external.alias"), _ => component);
        await using var otherProvider = otherServices.BuildServiceProvider();
        provider.GetWorkflowServices("first").GetRequiredKeyedService<IFlowNode>("first.original").ShouldBeSameAs(component);
        Should.Throw<ArgumentException>(() => provider.GetWorkflowServices("first").GetRequiredKeyedService<IFlowNode>("first.alias"));
        Should.Throw<ArgumentException>(() => provider.GetWorkflowServices("second").GetRequiredKeyedService<IFlowNode>("second.alias"));
        Should.Throw<ArgumentException>(() => otherProvider.GetWorkflowServices("external").GetRequiredKeyedService<IFlowNode>("external.alias"));
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
        services.AddComponentFactory(ComponentAddress.Parse("main.source"), _ => component);
        await using var provider = services.BuildServiceProvider();
        await using var execution = await provider.BuildApplicationAsync();
        await execution.DisposeAsync();
        await execution.DisposeAsync();
        component.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        component.DisposeCalls.ShouldBe(1);
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
        services.AddComponentFactory(ComponentAddress.Parse("main.first"), _ => first).AddComponentFactory(ComponentAddress.Parse("main.failing"), _ => failing).AddComponentFactory(ComponentAddress.Parse("main.unstarted"), _ => unstarted);
        await using var provider = services.BuildServiceProvider();
        var appExecution = await provider.BuildApplicationAsync();
        (await Should.ThrowAsync<InvalidOperationException>(() => appExecution.StartAsync().WaitAsync(Deadline))).ShouldBeSameAs(failure);
        first.StartCalls.ShouldBe(1);
        failing.StartCalls.ShouldBe(1);
        unstarted.StartCalls.ShouldBe(0);
        first.DisposeCalls.ShouldBe(1);
        failing.DisposeCalls.ShouldBe(1);
        unstarted.DisposeCalls.ShouldBe(1);
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
        services.AddComponentFactory(ComponentAddress.Parse("main.node"), _ => node);
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
        services.AddComponentFactory(ComponentAddress.Parse("main.node"), _ => node);
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
        services.AddComponentFactory(ComponentAddress.Parse("main.node"), _ => node);
        await using var provider = services.BuildServiceProvider();
        var app = await provider.BuildApplicationAsync();
        Task? nested = null;
        node.OnComplete = () => nested = app.DisposeAsync().AsTask();
        var first = app.DisposeAsync().AsTask();
        var second = app.DisposeAsync().AsTask();
        var completedEarly = first.IsCompleted || second.IsCompleted || nested!.IsCompleted;
        release.TrySetResult();
        foreach (var task in new[]
        {
            first,
            second,
            nested!
        }

        )
        {
            if (failure is null)
                await task.WaitAsync(Deadline);
            else
                (await Should.ThrowAsync<AggregateException>(() => task.WaitAsync(Deadline))).Flatten().InnerExceptions.ShouldContain(failure);
        }

        completedEarly.ShouldBeFalse();
        second.ShouldBeSameAs(first);
        nested.ShouldBeSameAs(first);
        node.DisposeCalls.ShouldBe(1);
        if (failure is null)
            await provider.DisposeAsync();
        else
            (await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask())).Flatten().InnerExceptions.ShouldContain(failure);
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
            if (++StartCalls == 1)
                OnStart?.Invoke();
            return Task.CompletedTask;
        }

        public void Complete()
        {
            if (++CompleteCalls != 1)
                return;
            OnComplete?.Invoke();
            _ = FinishAsync();
        }

        private async Task FinishAsync()
        {
            if (completionRelease is not null)
                await completionRelease;
            if (completionFailure is not null)
                _completed.TrySetException(completionFailure);
            else
                _completed.TrySetResult();
        }

        public void Fault(Exception error) => _completed.TrySetException(error);
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Start_before_build_is_rejected_without_closing_the_application()
    {
        var services = new ServiceCollection();
        var node = new CallbackNode();
        services.AddComponentFactory(ComponentAddress.Parse("main.node"), provider =>
        {
            Should.Throw<InvalidOperationException>(() => provider.GetRequiredService<FlowApplication>().StartAsync()).Message.ShouldContain("Build");
            return node;
        });
        await using var provider = services.BuildServiceProvider();
        var app = provider.GetRequiredService<FlowApplication>();
        Should.Throw<InvalidOperationException>(() => app.StartAsync()).Message.ShouldContain("Build");
        await provider.BuildApplicationAsync();
        await app.StartAsync();
        node.StartCalls.ShouldBe(1);
        await app.StopAsync();
    }

    [Fact]
    public async Task Aggregate_startup_failure_is_not_duplicated_by_cleanup()
    {
        var failure = new AggregateException(new InvalidOperationException("first"), new ArgumentException("second"));
        var services = new ServiceCollection();
        services.AddComponentFactory(ComponentAddress.Parse("main.source"), _ => new LifetimeProbe(failure));
        await using var provider = services.BuildServiceProvider();
        var app = await provider.BuildApplicationAsync();
        (await Record.ExceptionAsync(() => app.StartAsync().WaitAsync(Deadline))).ShouldBeSameAs(failure);
        await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Build_failure_keeps_only_independent_cleanup_failures()
    {
        var failure = new InvalidOperationException("build failed");
        var cleanupFailure = new ArgumentException("independent cleanup failure");
        var node = new CallbackNode(completionFailure: new AggregateException(failure, cleanupFailure, cleanupFailure));
        var services = new ServiceCollection();
        services.AddComponentFactory(ComponentAddress.Parse("main.node"), _ => node);
        services.AddSingleton<LinkFactory>(_ => throw failure);
        await using var provider = services.BuildServiceProvider();
        var error = await Should.ThrowAsync<AggregateException>(() => provider.BuildApplicationAsync().WaitAsync(Deadline));
        error.InnerExceptions.ShouldBe([failure, cleanupFailure]);
        node.DisposeCalls.ShouldBe(1);
        await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask());
        node.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task A_precanceled_start_leaves_the_application_available_for_composition_and_start()
    {
        var services = new ServiceCollection();
        services.AddComponentFactory(ComponentAddress.Parse("main.source"), _ => new LifetimeProbe());
        await using var provider = services.BuildServiceProvider();
        var app = provider.GetRequiredService<FlowApplication>();
        Should.Throw<OperationCanceledException>(() => app.StartAsync(new CancellationToken(canceled: true)));
        await provider.BuildApplicationAsync();
        var node = (LifetimeProbe)provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.source");
        await app.StartAsync();
        node.StartCalls.ShouldBe(1);
        await app.StopAsync();
    }

    [Fact]
    public async Task Fault_before_start_closes_composition_start_and_admission()
    {
        var services = new ServiceCollection();
        services.AddComponentFactory(ComponentAddress.Parse("main.node"), _ => new DrainEcho()).AddInputPort<DrainEcho>(ComponentAddress.Parse("main.node"), "Input", node => node.Input).AddOutputPort<DrainEcho>(ComponentAddress.Parse("main.node"), "Output", node => node.Output);
        await using var provider = services.BuildServiceProvider();
        var app = await provider.BuildApplicationAsync();
        var node = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.node");
        var input = provider.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Input");
        var failure = new InvalidOperationException("closed before startup");
        app.Fault(failure);
        app.Fault(new InvalidOperationException("later failure"));
        Should.Throw<InvalidOperationException>(() => provider.Link(PortAddress.Parse("main.node.Output"), PortAddress.Parse("main.node.Input")));
        Should.Throw<InvalidOperationException>(() => app.StartAsync());
        (await Should.ThrowAsync<InvalidOperationException>(() => node.Completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        (await input.SendAsync(FlowMessage.Create(JsonFlowValue.Null))).ShouldBeFalse();
        (await Should.ThrowAsync<InvalidOperationException>(() => app.StopAsync().WaitAsync(Deadline))).ShouldBeSameAs(failure);
        (await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask())).Flatten().InnerExceptions.ShouldContain(failure);
        await Should.ThrowAsync<Exception>(() => provider.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Fault_before_build_does_not_construct_registered_components()
    {
        var services = new ServiceCollection();
        var constructed = false;
        services.AddComponentFactory(ComponentAddress.Parse("main.node"), _ =>
        {
            constructed = true;
            return new LifetimeProbe();
        });
        await using var provider = services.BuildServiceProvider();
        var app = provider.GetRequiredService<FlowApplication>();
        var failure = new InvalidOperationException("closed before build");
        app.Fault(failure);
        await Should.ThrowAsync<AggregateException>(() => provider.BuildApplicationAsync());
        constructed.ShouldBeFalse();
        (await Should.ThrowAsync<InvalidOperationException>(() => app.StopAsync())).ShouldBeSameAs(failure);
        await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_link_attachment_releases_each_attached_link_once_even_when_unlinking_fails(bool failCleanup)
    {
        var failure = new InvalidOperationException("link attachment failed");
        var cleanupFailure = failCleanup ? new InvalidOperationException("unlink failed") : null;
        var output = new AttachmentProbe(failure, cleanupFailure);
        var source = new CallbackNode();
        var services = new ServiceCollection();
        services.AddComponentFactory(ComponentAddress.Parse("main.source"), _ => source).AddOutputPort<CallbackNode>(ComponentAddress.Parse("main.source"), "Output", _ => output);
        for (var index = 0; index < 3; index++)
        {
            var address = ComponentAddress.Create("main", $"sink{index}");
            services.AddComponentFactory(address, _ => new DrainSink()).AddInputPort<DrainSink>(address, "Input", node => node.Input);
            services.AddSingleton<LinkFactory>(ports => ports.Link(PortAddress.Parse("main.source.Output"), address.Port("Input")));
        }

        await using var provider = services.BuildServiceProvider();
        var app = await provider.BuildApplicationAsync();
        var error = await Should.ThrowAsync<Exception>(() => app.StartAsync().WaitAsync(Deadline));
        if (cleanupFailure is null)
            error.ShouldBeSameAs(failure);
        else
            ((AggregateException)error).Flatten().InnerExceptions.ShouldBe([failure, cleanupFailure]);
        output.Unlinks.ShouldBe([1, 1]);
        source.StartCalls.ShouldBe(0);
        source.DisposeCalls.ShouldBe(1);
        await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask());
        output.Unlinks.ShouldBe([1, 1]);
        await Should.ThrowAsync<Exception>(() => provider.DisposeAsync().AsTask());
    }

    private sealed class AttachmentProbe(Exception failure, Exception? cleanupFailure) : ISourceBlock<FlowMessage>
    {
        private int _attachments;
        public int[] Unlinks { get; } = new int[2];
        public Task Completion => Task.CompletedTask;

        public void Complete()
        {
        }

        public void Fault(Exception exception)
        {
        }

        public IDisposable LinkTo(ITargetBlock<FlowMessage> target, DataflowLinkOptions options)
        {
            var index = _attachments++;
            if (index == 2)
                throw failure;
            return new Attachment(() =>
            {
                Unlinks[index]++;
                if (index == 0 && cleanupFailure is not null)
                    throw cleanupFailure;
            });
        }

        public FlowMessage? ConsumeMessage(DataflowMessageHeader header, ITargetBlock<FlowMessage> target, out bool consumed) => throw new NotSupportedException();
        public bool ReserveMessage(DataflowMessageHeader header, ITargetBlock<FlowMessage> target) => throw new NotSupportedException();
        public void ReleaseReservation(DataflowMessageHeader header, ITargetBlock<FlowMessage> target) => throw new NotSupportedException();
        private sealed class Attachment(Action release) : IDisposable
        {
            public void Dispose() => release();
        }
    }

    [Fact]
    public async Task Native_DI_cleanup_stops_at_the_first_failure_without_repeating_disposal()
    {
        var firstFailure = new InvalidOperationException("first cleanup");
        var secondFailure = new InvalidOperationException("second cleanup");
        var first = new LifetimeProbe(disposalFailure: firstFailure);
        var second = new LifetimeProbe(disposalFailure: secondFailure);
        var services = new ServiceCollection();
        services.AddComponentFactory(ComponentAddress.Parse("main.first"), _ => first).AddComponentFactory(ComponentAddress.Parse("main.second"), _ => second);
        await using var provider = services.BuildServiceProvider();
        var app = await provider.BuildApplicationAsync();
        (await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask())).GetBaseException().ShouldBeSameAs(secondFailure);
        (await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask())).GetBaseException().ShouldBeSameAs(secondFailure);
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
        services.AddComponentFactory(ComponentAddress.Parse("main.active"), _ => component).AddInputPort<ActiveNode>(ComponentAddress.Parse("main.active"), "Input", node => node.Input).AddOutputPort<ActiveNode>(ComponentAddress.Parse("main.active"), "Output", node => node.Output);
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
        finally
        {
            release.TrySetResult();
        }

        await Should.ThrowAsync<AggregateException>(() => disposing.WaitAsync(Deadline));
        component.ResourceDisposed.ShouldBeTrue();
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
        services.AddComponentFactory(ComponentAddress.Parse("main.active"), _ => source).AddOutputPort<ActiveSource>(ComponentAddress.Parse("main.active"), "Output", node => node.Output);
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
        finally
        {
            release.TrySetResult();
        }

        await Should.ThrowAsync<AggregateException>(() => disposing.WaitAsync(Deadline));
        source.ResourceDisposed.ShouldBeTrue();
        await Should.ThrowAsync<Exception>(() => provider.DisposeAsync().AsTask());
        source.ResourceDisposed.ShouldBeTrue();
        source.ProducingExited.ShouldBeTrue();
    }

    [Fact]
    public async Task Completion_waits_for_unlinked_registered_input_and_output_ports()
    {
        var input = new ActionBlock<FlowMessage>(_ =>
        {
        });
        var output = new BroadcastBlock<FlowMessage>(message => message);
        var node = new CallbackNode();
        var address = ComponentAddress.Parse("main.node");
        var services = new ServiceCollection();
        services.AddComponentFactory(address, _ => node).AddInputPort<CallbackNode>(address, "Input", _ => input).AddOutputPort<CallbackNode>(address, "Output", _ => output).AddOutputPort<CallbackNode>(address, "Alias", _ => output);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var stopping = app.StopAsync();
        try
        {
            node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            stopping.IsCompleted.ShouldBeFalse();
            output.Complete();
            await output.Completion.WaitAsync(Deadline);
            app.Completion.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            input.Complete();
            output.Complete();
        }

        await stopping.WaitAsync(Deadline);
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fault_on_an_unlinked_registered_port_faults_the_application(bool input)
    {
        var port = new BufferBlock<FlowMessage>();
        var node = new CallbackNode();
        var address = ComponentAddress.Parse("main.node");
        var services = new ServiceCollection();
        services.AddComponentFactory(address, _ => node);
        if (input)
            services.AddInputPort<CallbackNode>(address, "Input", _ => port);
        else
            services.AddOutputPort<CallbackNode>(address, "Output", _ => port);
        await using var provider = services.BuildServiceProvider();
        var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var failure = new InvalidOperationException("unlinked port failed");
        ((IDataflowBlock)port).Fault(failure);
        (await Should.ThrowAsync<InvalidOperationException>(() => app.Completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        node.Completion.IsFaulted.ShouldBeTrue();
        await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask().WaitAsync(Deadline));
        node.DisposeCalls.ShouldBe(1);
        await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask());
        node.DisposeCalls.ShouldBe(1);
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
        services.AddComponentFactory(ComponentAddress.Parse("receiver.sink"), _ => sink).AddInputPort<DrainSink>(ComponentAddress.Parse("receiver.sink"), "Input", node => node.Input);
        foreach (var(name, node)in new[]
        {
            ("fast", fast),
            ("slow", slow)
        }

        )
            services.AddComponentFactory(ComponentAddress.Parse("senders." + name), _ => node).AddInputPort<DrainEcho>(ComponentAddress.Parse("senders." + name), "Input", value => value.Input).AddOutputPort<DrainEcho>(ComponentAddress.Parse("senders." + name), "Output", value => value.Output);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link(PortAddress.Parse("senders.fast.Output"), PortAddress.Parse("receiver.sink.Input"));
            ports.Link(PortAddress.Parse("senders.slow.Output"), PortAddress.Parse("receiver.sink.Input"));
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
        finally
        {
            release.TrySetResult();
        }

        await stopping.WaitAsync(Deadline);
        sink.Received.ShouldBe([first, second]);
        sink.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    private sealed class DrainEcho : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public DrainEcho(Task? release = null, TaskCompletionSource? entered = null)
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            this.release = release;
            this.entered = entered;
            Lifetime.Observe();
        }

        private readonly TaskCompletionSource? entered;
        private readonly Task? release;
        private async Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
        {
            entered?.TrySetResult();
            if (release is not null)
                await release;
            return[message];
        }
    }

    private sealed class DrainSink : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public DrainSink()
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
            Lifetime.Observe();
        }

        public List<FlowMessage> Received { get; } = [];
        public TaskCompletionSource FirstReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Task ProcessAsync(FlowMessage message)
        {
            Received.Add(message);
            FirstReceived.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class LifetimeProbe(Exception? startupFailure = null, Exception? disposalFailure = null) : IFlowSource
    {
        private readonly ActionBlock<FlowMessage> _input = new(_ =>
        {
        });
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
            try
            {
                await Completion;
            }
            catch
            {
            }

            if (disposalFailure is not null)
                throw disposalFailure;
        }
    }

    private sealed class ActiveNode : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public ActiveNode(Task release)
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
            this.release = release;
            Lifetime.Observe();
        }

        private readonly Task release;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ProcessingExited { get; private set; }
        public bool ResourceDisposed { get; private set; }

        private async Task ProcessAsync(FlowMessage message)
        {
            Entered.TrySetResult();
            await release;
            ProcessingExited = true;
        }

        protected override ValueTask OnDisposeAsync()
        {
            ResourceDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ActiveSource : FlowNode, IFlowSource
    {
        public Task ProductionCompletion => Lifetime.ProductionCompletion;

        public void StopProduction() => Lifetime.StopProduction();
        public Task StartAsync(CancellationToken cancellationToken = default) => Lifetime.StartAsync(cancellationToken);
        public ActiveSource(Task release)
        {
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, produce: RunAsync);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            this.release = release;
            Lifetime.Observe();
        }

        private readonly Task release;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ProducingExited { get; private set; }
        public bool ResourceDisposed { get; private set; }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await release;
            ProducingExited = true;
        }

        protected override ValueTask OnDisposeAsync()
        {
            ResourceDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
