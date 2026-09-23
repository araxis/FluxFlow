using System.Text.Json;
using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Application;
using FluxFlow.Core.Execution;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class FlowApplicationHostingTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Registration_is_lazy_and_hosts_native_DI_without_revision_services()
    {
        var calls = 0;
        var services = new ServiceCollection();
        services.AddComponentFactory("main.sink", _ => { calls++; return new Sink(); })
            .AddInputPort<Sink>("main.sink", "Input", node => node.Input);
        services.AddFluxFlow().ShouldBeSameAs(services);
        calls.ShouldBe(0);
        await using var provider = services.BuildServiceProvider();
        var appExecution = provider.GetRequiredService<FlowApplication>();
        var host = GetHost(provider);
        calls.ShouldBe(0);
        await host.StartAsync(default);
        calls.ShouldBe(1);
        (await provider.BuildApplicationAsync()).ShouldBeSameAs(appExecution);
        appExecution.Completion.IsCompleted.ShouldBeFalse();
        await host.StopAsync(default);
        await AssertStoppedObserverAsync(host);
        appExecution.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Host_starts_sources_after_routes_and_fans_out_the_same_message()
    {
        var messages = new[] { Message(3), Message(5), Message(8) };
        var source = new SequenceSource(messages);
        var left = new Sink();
        var right = new Sink();
        var services = new ServiceCollection();
        services.AddSingleton(_ => new FlowApplication(2));
        RegisterSource(services, source);
        RegisterSink(services, "main.left", left);
        RegisterSink(services, "reports.right", right);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.source.Output", "main.left.Input");
            ports.Link("main.source.Output", "reports.right.Input");
        });
        services.AddFluxFlow();
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
    public async Task Hosted_graph_delivers_messages_without_revision_services()
    {
        var services = new ServiceCollection();
        RegisterEcho(services, "main.echo");
        var received = OutputProbe.Register(services, "main.echo.Output");
        services.AddFluxFlow();
        await using var provider = services.BuildServiceProvider();
        var host = GetHost(provider);
        await host.StartAsync(default);
        var original = Message(42);

        (await provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.echo.Input").SendAsync(original)).ShouldBeTrue();
        var reply = await received.Messages.ReceiveAsync(Deadline);

        reply.Value.ShouldBeSameAs(original.Value);
        reply.MessageId.ShouldBe(original.MessageId);
        reply.TraceId.ShouldBe(original.TraceId);
        reply.CorrelationId.ShouldBe(original.CorrelationId);
        await host.StopAsync(default);
        await AssertStoppedObserverAsync(host);
    }

    [Fact]
    public async Task Host_stop_drains_accepted_work()
    {
        var entered = Signal();
        var release = Signal();
        var sink = new Sink(async _ => { entered.TrySetResult(); await release.Task; });
        var services = new ServiceCollection();
        RegisterSink(services, "main.sink", sink);
        services.AddFluxFlow();
        await using var provider = services.BuildServiceProvider();
        var appExecution = provider.GetRequiredService<FlowApplication>();
        var host = GetHost(provider);
        await host.StartAsync(default);
        var messages = new[] { Message(1), Message(2) };
        (await sink.Input.SendAsync(messages[0])).ShouldBeTrue();
        await entered.Task.WaitAsync(Deadline);
        (await sink.Input.SendAsync(messages[1])).ShouldBeTrue();
        Task stop;
        try
        {
            stop = host.StopAsync(default);
            stop.IsCompleted.ShouldBeFalse();
            appExecution.Completion.IsCompleted.ShouldBeFalse();
            (await sink.Input.SendAsync(Message(3))).ShouldBeFalse();
        }
        finally { release.TrySetResult(); }

        await stop.WaitAsync(Deadline);
        sink.Messages.ToArray().ShouldBe(messages);
        await AssertStoppedObserverAsync(host);
        sink.DisposeCalls.ShouldBe(0);
        await provider.DisposeAsync();
        sink.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Cancelled_stop_faults_graph_and_joins_observer()
    {
        var entered = Signal();
        var release = Signal();
        var sink = new Sink(async _ => { entered.TrySetResult(); await release.Task; });
        var services = new ServiceCollection();
        RegisterSink(services, "main.sink", sink);
        services.AddFluxFlow();
        await using var provider = services.BuildServiceProvider();
        var appExecution = provider.GetRequiredService<FlowApplication>();
        var host = GetHost(provider);
        await host.StartAsync(default);
        (await sink.Input.SendAsync(Message(1))).ShouldBeTrue();
        await entered.Task.WaitAsync(Deadline);
        using var cancellation = new CancellationTokenSource();
        var stop = host.StopAsync(cancellation.Token);
        cancellation.Cancel();
        try
        {
            await Should.ThrowAsync<OperationCanceledException>(() => stop.WaitAsync(Deadline));
            await AssertStoppedObserverAsync(host);
            (await sink.Input.SendAsync(Message(2))).ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        await Should.ThrowAsync<OperationCanceledException>(() => appExecution.Completion.WaitAsync(Deadline));
        await Should.ThrowAsync<AggregateException>(() => appExecution.DisposeAsync().AsTask());
        sink.DisposeCalls.ShouldBe(0);
        var disposalError = await Should.ThrowAsync<Exception>(() => provider.DisposeAsync().AsTask());
        IEnumerable<Exception> errors = disposalError is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [disposalError];
        errors.OfType<OperationCanceledException>().Select(error => error.CancellationToken).ShouldContain(cancellation.Token);
        sink.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Graph_failure_faults_host_execution()
    {
        var failure = new InvalidOperationException("processing failed");
        var sink = new Sink(_ => Task.FromException(failure));
        var services = new ServiceCollection();
        RegisterSink(services, "main.sink", sink);
        services.AddFluxFlow();
        await using var provider = services.BuildServiceProvider();
        var appExecution = provider.GetRequiredService<FlowApplication>();
        var host = GetHost(provider);
        await host.StartAsync(default);

        (await sink.Input.SendAsync(Message(1))).ShouldBeTrue();

        (await Should.ThrowAsync<InvalidOperationException>(() => host.ExecuteTask!.WaitAsync(Deadline)))
            .ShouldBeSameAs(failure);
        (await Should.ThrowAsync<InvalidOperationException>(() => host.StopAsync(default)))
            .ShouldBeSameAs(failure);
        appExecution.Completion.IsFaulted.ShouldBeTrue();
        await Should.ThrowAsync<AggregateException>(() => appExecution.DisposeAsync().AsTask());
        sink.DisposeCalls.ShouldBe(0);
        (await Should.ThrowAsync<InvalidOperationException>(() => provider.DisposeAsync().AsTask())).ShouldBeSameAs(failure);
        sink.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_graph_fails_host_start()
    {
        var source = new SequenceSource([Message(1)]);
        var services = new ServiceCollection();
        RegisterSource(services, source);
        RegisterEcho(services, "main.loop");
        services.AddSingleton<LinkFactory>(ports =>
        {

            ports.Link("main.loop.Output", "main.loop.Input");
        });
        services.AddFluxFlow();
        TestSink.Register(services, "main.source.Output");
        await using var provider = services.BuildServiceProvider();
        var host = GetHost(provider);

        var error = await Should.ThrowAsync<InvalidOperationException>(() => host.StartAsync(default));

        error.Message.ShouldContain("cycle");
        source.Runs.ShouldBe(0);
        host.ExecuteTask.ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_owns_lazily_constructed_components(bool resolveHost)
    {
        Sink? sink = null;
        var services = new ServiceCollection();
        services.AddComponentFactory("main.sink", _ => sink = new Sink())
            .AddInputPort<Sink>("main.sink", "Input", node => node.Input);
        services.AddFluxFlow();
        var provider = services.BuildServiceProvider();
        if (resolveHost)
        {
            var host = GetHost(provider);
            await host.StartAsync(default);
            await host.StopAsync(default);
        }

        await provider.DisposeAsync();
        if (resolveHost) sink.ShouldNotBeNull().DisposeCalls.ShouldBe(1);
        else sink.ShouldBeNull();
        await provider.DisposeAsync();
        if (resolveHost) sink!.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Cancelled_start_does_not_run_sources_and_allows_a_fresh_start()
    {
        var source = new SequenceSource([Message(1), Message(2)]);
        var sink = new Sink();
        var services = new ServiceCollection();
        RegisterSource(services, source);
        RegisterSink(services, "main.sink", sink);
        services.AddSingleton<LinkFactory>(ports => ports.Link("main.source.Output", "main.sink.Input"));
        services.AddFluxFlow();
        await using var provider = services.BuildServiceProvider();
        var host = GetHost(provider);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => host.StartAsync(cancellation.Token));

        source.Runs.ShouldBe(0);
        host.ExecuteTask.ShouldBeNull();
        await host.StartAsync(default);
        await host.ExecuteTask!.WaitAsync(Deadline);
        source.Runs.ShouldBe(1);
        sink.Messages.Select(message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([1, 2]);
        await host.StopAsync(default);
    }

    [Fact]
    public void Duplicate_host_registration_fails_without_mutation()
    {
        var services = new ServiceCollection().AddFluxFlow();
        var registrations = services.ToArray();
        Should.Throw<InvalidOperationException>(() => services.AddFluxFlow());
        services.ToArray().ShouldBe(registrations);
    }

    [Fact]
    public void Null_registration_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => FlowApplicationHostingExtensions.AddFluxFlow(null!))
            .ParamName.ShouldBe("services");
    }

    private static void RegisterSink(IServiceCollection services, string address, Sink sink)
        => services.AddComponentFactory(address, _ => sink)
            .AddInputPort<Sink>(address, "Input", node => node.Input);

    private static void RegisterEcho(IServiceCollection services, string address)
        => services.AddComponentFactory(address, _ => new Echo())
            .AddInputPort<Echo>(address, "Input", node => node.Input)
            .AddOutputPort<Echo>(address, "Output", node => node.Output)
            .AddEventPort<Echo>(address, node => node.Events);

    private static void RegisterSource(IServiceCollection services, SequenceSource source)
        => services.AddComponentFactory("main.source", _ => source)
            .AddOutputPort<SequenceSource>("main.source", "Output", node => node.Output)
            .AddEventPort<SequenceSource>("main.source", node => node.Events);

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
    private static FlowMessage Message(int value) => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

private sealed class SequenceSource(FlowMessage[] messages) : FlowSource(outputCapacity: 2)
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

    private sealed class Sink : IFlowNode
    {
        private readonly ActionBlock<FlowMessage> _input;
        public ITargetBlock<FlowMessage> Input => _input;
        public Sink(Func<FlowMessage, Task>? process = null)
        {
            _input = new ActionBlock<FlowMessage>(async message =>
            {
                if (process is not null) await process(message);
                Messages.Enqueue(message);
            }, new ExecutionDataflowBlockOptions { BoundedCapacity = 2 });
        }
        public ConcurrentQueue<FlowMessage> Messages { get; } = new();
        public int DisposeCalls { get; private set; }
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
