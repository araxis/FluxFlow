using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Json.Model;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class FlowApplicationHostingTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Registration_hosts_the_supplied_graph_without_revision_services()
    {
        await using var app = new FlowApplication();
        app.AddWorkflow("main").Add("sink", new Sink());
        var services = new ServiceCollection();

        services.AddFluxFlow(app).ShouldBeSameAs(services);
        services.Select(item => item.ServiceType).ShouldBe([typeof(FlowApplication), typeof(IHostedService)]);
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<FlowApplication>().ShouldBeSameAs(app);
        provider.GetService<FluxFlowApplication>().ShouldBeNull();
        provider.GetService<IApplicationDefinitionSource>().ShouldBeNull();
        provider.GetService<ApplicationRuntimeAssembler>().ShouldBeNull();
        var host = GetHost(provider);
        await host.StartAsync(default);
        app.Completion.IsCompleted.ShouldBeFalse();
        await host.StopAsync(default);
        await AssertStoppedObserverAsync(host);
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Host_starts_sources_after_routes_and_fans_out_the_same_message()
    {
        await using var app = new FlowApplication(linkCapacity: 2);
        var messages = new[] { Message(3), Message(5), Message(8) };
        var source = new SequenceSource(messages);
        var left = new Sink();
        var right = new Sink();
        var main = app.AddWorkflow("main");
        var output = main.Add("source", source).Output;
        output.LinkTo(main.Add("left", left).Input);
        output.LinkTo(app.AddWorkflow("reports").Add("right", right).Input);
        var services = new ServiceCollection().AddFluxFlow(app);
        await using var provider = services.BuildServiceProvider();
        var host = GetHost(provider);
        source.Runs.ShouldBe(0);

        await host.StartAsync(default);
        await host.ExecuteTask!.WaitAsync(Deadline);
        await host.StopAsync(default);

        source.Runs.ShouldBe(1);
        left.Messages.Count.ShouldBe(3);
        right.Messages.Count.ShouldBe(3);
        for (var index = 0; index < messages.Length; index++)
        {
            left.Messages.ElementAt(index).ShouldBeSameAs(messages[index]);
            right.Messages.ElementAt(index).ShouldBeSameAs(messages[index]);
        }
        left.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        right.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Hosted_graph_supports_request_reply_without_revision_services()
    {
        await using var app = new FlowApplication();
        var echo = app.AddWorkflow("main").Add("echo", new Echo());
        app.RepliesFrom(echo.Output);
        await using var provider = new ServiceCollection().AddFluxFlow(app).BuildServiceProvider();
        var host = GetHost(provider);
        await host.StartAsync(default);
        var original = Message(42);

        var reply = await app.RequestAsync(echo.Input, echo.Output, original, Deadline);

        reply.Value.ShouldBeSameAs(original.Value);
        reply.MessageId.ShouldBe(original.MessageId);
        reply.TraceId.ShouldBe(original.TraceId);
        reply.CorrelationId.ShouldNotBeNull();
        app.PendingRequestCount.ShouldBe(0);
        await host.StopAsync(default);
        await AssertStoppedObserverAsync(host);
    }

    [Fact]
    public async Task Host_stop_drains_accepted_work()
    {
        var entered = Signal();
        var release = Signal();
        await using var app = new FlowApplication();
        var sink = new Sink(async _ => { entered.TrySetResult(); await release.Task; });
        var input = app.AddWorkflow("main").Add("sink", sink).Input;
        await using var provider = new ServiceCollection().AddFluxFlow(app).BuildServiceProvider();
        var host = GetHost(provider);
        await host.StartAsync(default);
        var messages = new[] { Message(1), Message(2) };
        (await app.SendAsync(input, messages[0])).ShouldBeTrue();
        await entered.Task.WaitAsync(Deadline);
        (await app.SendAsync(input, messages[1])).ShouldBeTrue();
        Task stop;
        try
        {
            stop = host.StopAsync(default);
            stop.IsCompleted.ShouldBeFalse();
            app.Completion.IsCompleted.ShouldBeFalse();
            await Should.ThrowAsync<InvalidOperationException>(() => app.SendAsync(input, Message(3)).AsTask());
        }
        finally { release.TrySetResult(); }

        await stop.WaitAsync(Deadline);
        sink.Messages.ToArray().ShouldBe(messages);
        await AssertStoppedObserverAsync(host);
        sink.DisposeCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Cancelled_stop_faults_graph_and_joins_observer()
    {
        var entered = Signal();
        var release = Signal();
        var app = new FlowApplication();
        var sink = new Sink(async _ => { entered.TrySetResult(); await release.Task; });
        var input = app.AddWorkflow("main").Add("sink", sink).Input;
        await using var provider = new ServiceCollection().AddFluxFlow(app).BuildServiceProvider();
        var host = GetHost(provider);
        await host.StartAsync(default);
        (await app.SendAsync(input, Message(1))).ShouldBeTrue();
        await entered.Task.WaitAsync(Deadline);
        using var cancellation = new CancellationTokenSource();
        var stop = host.StopAsync(cancellation.Token);
        cancellation.Cancel();
        try
        {
            await Should.ThrowAsync<OperationCanceledException>(() => stop.WaitAsync(Deadline));
            await AssertStoppedObserverAsync(host);
            await Should.ThrowAsync<InvalidOperationException>(() => app.SendAsync(input, Message(2)).AsTask());
        }
        finally { release.TrySetResult(); }
        await Should.ThrowAsync<OperationCanceledException>(() => app.Completion.WaitAsync(Deadline));
        await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask());
        sink.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Graph_failure_faults_host_execution()
    {
        var failure = new InvalidOperationException("processing failed");
        var app = new FlowApplication();
        var sink = new Sink(_ => Task.FromException(failure));
        var input = app.AddWorkflow("main").Add("sink", sink).Input;
        await using var provider = new ServiceCollection().AddFluxFlow(app).BuildServiceProvider();
        var host = GetHost(provider);
        await host.StartAsync(default);

        (await app.SendAsync(input, Message(1))).ShouldBeTrue();

        (await Should.ThrowAsync<InvalidOperationException>(() => host.ExecuteTask!.WaitAsync(Deadline)))
            .ShouldBeSameAs(failure);
        (await Should.ThrowAsync<InvalidOperationException>(() => host.StopAsync(default)))
            .ShouldBeSameAs(failure);
        app.Completion.IsFaulted.ShouldBeTrue();
        await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask());
        sink.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_graph_fails_host_start()
    {
        await using var app = new FlowApplication();
        var source = new SequenceSource([Message(1)]);
        var workflow = app.AddWorkflow("main");
        workflow.Add("source", source).Output.DiscardUnmatched();
        var loop = workflow.Add("loop", new Echo());
        loop.Output.LinkTo(loop.Input);
        await using var provider = new ServiceCollection().AddFluxFlow(app).BuildServiceProvider();
        var host = GetHost(provider);

        var error = await Should.ThrowAsync<InvalidOperationException>(() => host.StartAsync(default));

        error.Message.ShouldContain("cycle");
        source.Runs.ShouldBe(0);
        host.ExecuteTask.ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_disposal_does_not_dispose_the_borrowed_graph(bool resolveHost)
    {
        await using var app = new FlowApplication();
        var sink = new Sink();
        app.AddWorkflow("main").Add("sink", sink);
        var provider = new ServiceCollection().AddFluxFlow(app).BuildServiceProvider();
        if (resolveHost)
        {
            var host = GetHost(provider);
            await host.StartAsync(default);
            await host.StopAsync(default);
        }

        await provider.DisposeAsync();
        sink.DisposeCalls.ShouldBe(0);
        await app.DisposeAsync();
        sink.DisposeCalls.ShouldBe(1);
        await app.DisposeAsync();
        sink.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Cancelled_start_does_not_run_sources_and_allows_a_fresh_start()
    {
        await using var app = new FlowApplication();
        var source = new SequenceSource([Message(1), Message(2)]);
        var sink = new Sink();
        var workflow = app.AddWorkflow("main");
        workflow.Add("source", source).Output.LinkTo(workflow.Add("sink", sink).Input);
        await using var provider = new ServiceCollection().AddFluxFlow(app).BuildServiceProvider();
        var host = GetHost(provider);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => host.StartAsync(cancellation.Token));

        source.Runs.ShouldBe(0);
        host.ExecuteTask.ShouldBeNull();
        await host.StartAsync(default);
        await host.ExecuteTask!.WaitAsync(Deadline);
        source.Runs.ShouldBe(1);
        sink.Messages.Select(message => message.Value!.Deserialize<int>()).ShouldBe([1, 2]);
        await host.StopAsync(default);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("updates-first")]
    [InlineData("core-first")]
    [InlineData("core-first-generic")]
    public async Task Conflicting_host_registrations_fail_without_mutation(string order)
    {
        await using var app = new FlowApplication();
        var services = new ServiceCollection();
        if (order == "updates-first") services.AddFluxFlowUpdates(new ApplicationDefinition());
        else services.AddFluxFlow(app);
        var registrations = services.ToArray();

        Should.Throw<InvalidOperationException>(() =>
        {
            if (order == "core-first") services.AddFluxFlowUpdates(new ApplicationDefinition());
            else if (order == "core-first-generic") services.AddFluxFlowUpdates<UnloadedDefinitionSource>();
            else services.AddFluxFlow(app);
        }).Message.ShouldContain("already registered");

        services.ToArray().ShouldBe(registrations);
    }

    [Fact]
    public async Task Null_registration_is_rejected()
    {
        await using var app = new FlowApplication();
        Should.Throw<ArgumentNullException>(() => FlowApplicationHostingExtensions.AddFluxFlow(null!, app))
            .ParamName.ShouldBe("services");
        var services = new ServiceCollection();
        Should.Throw<ArgumentNullException>(() => services.AddFluxFlow(null!))
            .ParamName.ShouldBe("application");
        services.ShouldBeEmpty();
    }

    private static BackgroundService GetHost(ServiceProvider provider)
        => provider.GetServices<IHostedService>().Single().ShouldBeAssignableTo<BackgroundService>()!;

    private static async Task AssertStoppedObserverAsync(BackgroundService host)
    {
        var execution = host.ExecuteTask.ShouldNotBeNull();
        try { await execution.WaitAsync(Deadline); }
        catch (OperationCanceledException) when (execution.IsCanceled) { }
        // Immediate stop can cancel the scheduled observer before it begins executing.
        execution.IsCompleted.ShouldBeTrue();
        execution.IsFaulted.ShouldBeFalse();
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static FlowMessage Message(int value) => FlowMessage.Create(FlowValue.From(value));

    private sealed class UnloadedDefinitionSource : IApplicationDefinitionSource
    {
        public ValueTask<FluxFlow.Composition.ApplicationSetup> LoadAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Definition loading is not part of registration.");
    }

    private sealed class SequenceSource(FlowMessage[] messages) : FlowSource(new FlowSourceOptions { OutputCapacity = 2 })
    {
        public int Runs { get; private set; }
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            Runs++;
            foreach (var message in messages) await EmitAsync(message, cancellationToken);
        }
    }

    private sealed class Echo : FlowNode
    {
        protected override Task ProcessAsync(FlowMessage message) => EmitAsync(message).AsTask();
    }

    private sealed class Sink : IFlowComponent
    {
        private readonly ActionBlock<FlowMessage> _input;
        public Sink(Func<FlowMessage, Task>? process = null)
        {
            _input = new ActionBlock<FlowMessage>(async message =>
            {
                if (process is not null) await process(message);
                Messages.Enqueue(message);
            }, new ExecutionDataflowBlockOptions { BoundedCapacity = 2 });
            Ports = new FlowPorts(inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = _input });
        }
        public ConcurrentQueue<FlowMessage> Messages { get; } = new();
        public int DisposeCalls { get; private set; }
        public FlowPorts Ports { get; }
        public Task Completion => _input.Completion;
        public void Complete() => _input.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)_input).Fault(error);
        public async ValueTask DisposeAsync()
        {
            DisposeCalls++;
            Complete();
            await Completion;
        }
    }
}
