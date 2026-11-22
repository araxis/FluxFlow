using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class WorkflowLifecycleTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Restart_recreates_nodes_ports_and_local_resources_without_recreating_shared_resources()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowResource(ApplicationAddress.Resource("shared"), _ => new Resource());
        services.AddFluxFlowResource(ApplicationAddress.WorkflowResource("a", "local"), _ => new Resource());
        Register(services, "a.node", provider => new Node(
            provider.GetRequiredFlowResource<Resource>("local", "a"),
            provider.GetRequiredFlowResource<Resource>("shared")));
        Register(services, "b.node", _ => new Node());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var workflow = app.Workflows.Single(item => item.Name == "a");
        var first = (Node)app.GetComponent(ComponentAddress.Parse("a.node"));
        var other = app.GetComponent(ComponentAddress.Parse("b.node"));
        using var probe = provider.ObserveOutput(PortAddress.Parse("a.node.Output"));
        using var events = provider.ObserveOutput(PortAddress.Parse("a.node.Events"));
        var message = FlowMessage.Create(JsonFlowValue.Null);
        await first.Input.SendAsync(message);
        (await probe.Messages.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        (await events.Messages.ReceiveAsync(Deadline)).Headers[FlowEventHeaders.Source].Json.GetString().ShouldBe("a.node");
        await Task.WhenAll(workflow.StopAsync(), workflow.StopAsync()).WaitAsync(Deadline);
        await probe.Messages.Completion.WaitAsync(Deadline);
        await events.Messages.Completion.WaitAsync(Deadline);
        first.Disposals.ShouldBe(1);
        first.Local!.Disposals.ShouldBe(1);
        first.Shared!.Disposals.ShouldBe(0);
        other.Completion.IsCompleted.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => workflow.Services);
        await workflow.StartAsync().WaitAsync(Deadline);
        await workflow.StartAsync();
        var second = (Node)app.GetComponent(ComponentAddress.Parse("a.node"));
        second.ShouldNotBeSameAs(first);
        second.Input.ShouldNotBeSameAs(first.Input);
        second.Local.ShouldNotBeSameAs(first.Local);
        second.Shared.ShouldBeSameAs(first.Shared);
        app.GetComponent(ComponentAddress.Parse("b.node")).ShouldBeSameAs(other);
        using var restartedProbe = provider.ObserveOutput(PortAddress.Parse("a.node.Output"));
        using var restartedEvents = provider.ObserveOutput(PortAddress.Parse("a.node.Events"));
        await second.Input.SendAsync(message);
        (await restartedProbe.Messages.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        (await restartedEvents.Messages.ReceiveAsync(Deadline)).Headers[FlowEventHeaders.Source].Json.GetString().ShouldBe("a.node");
        await app.StopAsync().WaitAsync(Deadline);
        second.Disposals.ShouldBe(1);
        second.Local!.Disposals.ShouldBe(1);
        first.Shared.Disposals.ShouldBe(0);
        await provider.DisposeAsync();
        first.Shared.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Stop_drains_all_producers_and_rebinds_internal_links_on_restart()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        Register(services, "main.left", _ => new Node(process: async () => { entered.TrySetResult(); await release.Task; }));
        Register(services, "main.right", _ => new Node());
        Register(services, "main.sink", _ => new Node());
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link(PortAddress.Parse("main.left.Output"), PortAddress.Parse("main.sink.Input"));
            ports.Link(PortAddress.Parse("main.right.Output"), PortAddress.Parse("main.sink.Input"));
        });
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var workflow = app.Workflows.Single();
        var left = (Node)app.GetComponent(ComponentAddress.Parse("main.left"));
        var sink = (Node)app.GetComponent(ComponentAddress.Parse("main.sink"));
        var message = FlowMessage.Create(JsonFlowValue.Null);
        await left.Input.SendAsync(message);
        await entered.Task.WaitAsync(Deadline);
        var stopping = workflow.StopAsync();
        try
        {
            stopping.IsCompleted.ShouldBeFalse();
            sink.Completion.IsCompleted.ShouldBeFalse();
            Should.Throw<InvalidOperationException>(() => workflow.StartAsync()).Message.ShouldContain("still stopping");
            (await left.Input.SendAsync(message)).ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        await stopping.WaitAsync(Deadline);
        sink.Received.ShouldBe(1);
        await workflow.StartAsync();
        var nextLeft = (Node)app.GetComponent(ComponentAddress.Parse("main.left"));
        var nextSink = (Node)app.GetComponent(ComponentAddress.Parse("main.sink"));
        await nextLeft.Input.SendAsync(message);
        await workflow.StopAsync().WaitAsync(Deadline);
        nextSink.Received.ShouldBe(1);
        sink.Received.ShouldBe(1);
    }

    [Fact]
    public async Task Cross_workflow_links_reject_individual_changes_before_stopping_any_component()
    {
        var services = new ServiceCollection();
        Register(services, "a.node", _ => new Node());
        Register(services, "b.node", _ => new Node());
        services.AddSingleton<LinkFactory>(ports => ports.Link(PortAddress.Parse("a.node.Output"), PortAddress.Parse("b.node.Input")));
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        foreach (var workflow in app.Workflows)
        {
            Should.Throw<InvalidOperationException>(() => workflow.StopAsync()).Message.ShouldContain("cross-workflow");
            Should.Throw<InvalidOperationException>(() => workflow.StartAsync()).Message.ShouldContain("cross-workflow");
            workflow.Completion.IsCompleted.ShouldBeFalse();
        }
        var source = (Node)app.GetComponent(ComponentAddress.Parse("a.node"));
        var sink = (Node)app.GetComponent(ComponentAddress.Parse("b.node"));
        await source.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        await app.StopAsync().WaitAsync(Deadline);
        sink.Received.ShouldBe(1);
    }

    [Fact]
    public async Task Host_root_and_unrelated_scopes_cannot_create_duplicate_workflow_components()
    {
        var services = new ServiceCollection();
        var calls = 0;
        services.AddFluxFlowResource(ApplicationAddress.WorkflowResource("main", "local"), _ => new Resource());
        services.AddFlowWorkflow("other");
        Register(services, "main.node", _ => { calls++; return new Node(); });
        await using var provider = services.BuildServiceProvider();
        Should.Throw<InvalidOperationException>(() => provider.GetRequiredKeyedService<IFlowNode>("main.node"));
        await using var unrelated = provider.CreateAsyncScope();
        Should.Throw<InvalidOperationException>(() => unrelated.ServiceProvider.GetRequiredKeyedService<IFlowNode>("main.node"));
        Should.Throw<InvalidOperationException>(() => provider.GetRequiredFlowResource<Resource>("local", "main"));
        Should.Throw<InvalidOperationException>(() => provider.GetWorkflowServices("other").GetRequiredFlowResource<Resource>("local", "main"));
        calls.ShouldBe(0);
        await using var app = await provider.BuildApplicationAsync();
        calls.ShouldBe(1);
    }

    [Fact]
    public async Task Application_stop_reports_failures_from_each_workflow_scope()
    {
        var services = new ServiceCollection();
        var firstError = new InvalidOperationException("First resource disposal failed.");
        var secondError = new InvalidOperationException("Second resource disposal failed.");
        foreach (var (name, error) in new[] { ("a", firstError), ("b", secondError) })
        {
            services.AddFluxFlowResource(ApplicationAddress.WorkflowResource(name, "local"), _ => new Resource(error));
            Register(services, name + ".node", provider => new Node(provider.GetRequiredFlowResource<Resource>("local", name)));
        }
        var provider = services.BuildServiceProvider();
        try
        {
            var app = await provider.BuildApplicationAsync();
            await app.StartAsync();
            var nodes = app.Workflows.Select(workflow => (Node)workflow.Services.GetRequiredKeyedService<IFlowNode>(workflow.Name + ".node")).ToArray();
            var failure = await Should.ThrowAsync<AggregateException>(() => app.StopAsync().WaitAsync(Deadline));
            failure.Flatten().InnerExceptions.ShouldBe([firstError, secondError], ignoreOrder: true);
            foreach (var node in nodes)
            {
                node.Disposals.ShouldBe(1);
                node.Local!.Disposals.ShouldBe(1);
            }
        }
        finally
        {
            var failure = await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask().WaitAsync(Deadline));
            failure.Flatten().InnerExceptions.ShouldBe([firstError, secondError], ignoreOrder: true);
        }
    }

    private static void Register(IServiceCollection services, string address, ComponentFactory factory)
    {
        var component = ComponentAddress.Parse(address);
        services.AddComponentFactory(component, factory)
            .AddInputPort<Node>(component, "Input", node => node.Input)
            .AddOutputPort<Node>(component, "Output", node => node.Output)
            .AddEventPort<Node>(component, node => node.Events);
    }

    private sealed class Resource(Exception? disposalError = null) : IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose()
        {
            Disposals++;
            if (disposalError is not null) throw disposalError;
        }
    }

    private sealed class Node : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }
        public Resource? Local { get; }
        public Resource? Shared { get; }
        public int Received { get; private set; }
        public int Disposals { get; private set; }
        public Node(Resource? local = null, Resource? shared = null, Func<Task>? process = null)
        {
            Local = local;
            Shared = shared;
            Output = new BroadcastBlock<FlowMessage>(static message => message);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this);
            var input = new ActionBlock<FlowMessage>(async message =>
            {
                if (process is not null) await process();
                Received++;
                await EmitAsync(message);
                EmitEvent(message);
            });
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }
        protected override ValueTask OnDisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
