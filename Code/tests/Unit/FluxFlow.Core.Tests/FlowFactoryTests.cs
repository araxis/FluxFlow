using FluxFlow.Application.Addressing;
using FluxFlow.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using FluxFlow.Application.Execution;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Core.Tests;
public sealed class FlowFactoryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    [Fact]
    public async Task Workflows_are_provider_owned_singletons_discoverable_before_build_without_creating_components()
    {
        var created = 0;
        var services = new ServiceCollection().AddFlowWorkflow("empty").AddComponentFactory(ComponentAddress.Parse("main.first"), _ =>
        {
            created++;
            return new Probe();
        }).AddComponentFactory(ComponentAddress.Parse("main.second"), _ =>
        {
            created++;
            return new Probe();
        });
        var original = services.ToArray();
        Should.Throw<ArgumentException>(() => services.AddFlowWorkflow("main"));
        services.ShouldBe(original);
        await using var provider = services.BuildServiceProvider();
        await using var otherProvider = services.BuildServiceProvider();
        var workflow = provider.GetRequiredKeyedService<FlowWorkflow>("main");
        provider.GetServices<FlowWorkflow>().Select(item => item.Name).ShouldBe(["empty", "main"]);
        provider.GetServices<FlowWorkflow>().Last().ShouldBeSameAs(workflow);
        otherProvider.GetRequiredKeyedService<FlowWorkflow>("main").ShouldNotBeSameAs(workflow);
        created.ShouldBe(0);
        await using var app = await provider.BuildApplicationAsync();
        app.Workflows.Single(item => item.Name == "main").ShouldBeSameAs(workflow);
        (await provider.BuildApplicationAsync()).ShouldBeSameAs(app);
        created.ShouldBe(2);
        var first = (Probe)provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.first");
        await app.DisposeAsync();
        first.Disposals.ShouldBe(1);
        await provider.DisposeAsync();
        first.Disposals.ShouldBe(1);
        created.ShouldBe(2);
    }

    [Fact]
    public async Task Disposing_an_unbuilt_application_does_not_resolve_components_or_events()
    {
        var services = new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("main.node"), _ => throw new Exception("Must not construct")).AddEventPort<Probe>(ComponentAddress.Parse("main.node"), "Events", _ => throw new Exception("Must not select"));
        await using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<FlowApplication>();
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task A_stopped_application_cannot_construct_registered_components()
    {
        var created = 0;
        var services = new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("main.node"), _ =>
        {
            created++;
            return new Probe();
        });
        await using var provider = services.BuildServiceProvider();
        var app = provider.GetRequiredService<FlowApplication>();
        await app.StopAsync();
        await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync());
        created.ShouldBe(0);
    }

    [Fact]
    public async Task Build_requires_a_registered_application()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        await Should.ThrowAsync<InvalidOperationException>(async () => await provider.BuildApplicationAsync());
    }

    [Fact]
    public async Task Build_preserves_registered_application_identity()
    {
        var services = new ServiceCollection();
        services.AddSingleton<FlowApplication>();
        await using var provider = services.BuildServiceProvider();
        var first = await provider.BuildApplicationAsync();
        (await provider.BuildApplicationAsync()).ShouldBeSameAs(first);
    }

    [Fact]
    public async Task Build_composes_once_and_start_only_starts_sources()
    {
        var order = new List<string>();
        var services = new ServiceCollection().AddFlowWorkflow("empty");
        foreach (var name in new[]
        {
            "first",
            "second"
        }

        )
        {
            var address = $"a.{name}";
            services.AddComponentFactory(ComponentAddress.Parse(address), provider =>
            {
                order.Add(name);
                return new Probe(() => order.Add($"start-{name}"));
            });
            services.AddInputPort<Probe>(ComponentAddress.Parse(address), "Input", node => node.Input);
        }

        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.GetWorkflowServices("a").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("a.first.Input").ShouldNotBeNull();
            ports.GetWorkflowServices("a").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("a.second.Input").ShouldNotBeNull();
            order.Add("links");
        });
        await using var provider = services.BuildServiceProvider();
        services.Single(item => item.IsKeyedService && item.ServiceType == typeof(IFlowNode) && Equals(item.ServiceKey, "a.first")).KeyedImplementationFactory.ShouldNotBeNull();
        order.ShouldBeEmpty();
        await using var app = await provider.BuildApplicationAsync();
        order.ShouldBe(["first", "second", "links"]);
        app.Workflows.Count.ShouldBe(2);
        var starting = app.StartAsync();
        app.StartAsync().ShouldBeSameAs(starting);
        await starting.WaitAsync(Deadline);
        order.ShouldBe(["first", "second", "links", "start-first", "start-second"]);
        app.Workflows.Select(workflow => workflow.Name).ShouldContain("empty");
        app.GetComponent(ComponentAddress.Parse("a.first")).ShouldBeOfType<Probe>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_factory_or_link_stops_components_before_provider_cleanup(bool failLink)
    {
        var node = new Probe();
        var failure = new InvalidOperationException("factory failed");
        var services = new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("main.first"), _ => node);
        services.AddComponentFactory(ComponentAddress.Parse("main.second"), _ => failLink ? new Probe() : throw failure);
        services.AddSingleton(new LinkFactory(_ => throw failure));
        await using var provider = services.BuildServiceProvider();
        (await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync().WaitAsync(Deadline))).ShouldBeSameAs(failure);
        node.Starts.ShouldBe(0);
        node.Disposals.ShouldBe(1);
        node.Completion.IsCompleted.ShouldBeTrue();
        await provider.DisposeAsync();
        node.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Cancellation_is_checked_between_factories_and_stops_the_returned_instance()
    {
        using var cancellation = new CancellationTokenSource();
        var node = new Probe();
        var secondCreated = false;
        var linked = false;
        var services = new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("main.first"), _ =>
        {
            cancellation.Cancel();
            return node;
        });
        services.AddComponentFactory(ComponentAddress.Parse("main.second"), _ =>
        {
            secondCreated = true;
            return new Probe();
        });
        services.AddSingleton<LinkFactory>(_ =>
        {
            linked = true;
        });
        await using var provider = services.BuildServiceProvider();
        await Should.ThrowAsync<OperationCanceledException>(() => provider.BuildApplicationAsync(cancellationToken: cancellation.Token).WaitAsync(Deadline));
        node.Starts.ShouldBe(0);
        node.Disposals.ShouldBe(1);
        node.Completion.IsCompleted.ShouldBeTrue();
        await provider.DisposeAsync();
        node.Disposals.ShouldBe(1);
        secondCreated.ShouldBeFalse();
        linked.ShouldBeFalse();
    }

    [Fact]
    public async Task Failed_port_selection_stops_the_component_and_preserves_provider_ownership()
    {
        var failure = new InvalidOperationException("port failed");
        var node = new Probe();
        var services = new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("main.node"), _ => node);
        services.AddInputPort<Probe>(ComponentAddress.Parse("main.node"), "Input", _ => throw failure);
        await using var provider = services.BuildServiceProvider();
        (await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync())).ShouldBeSameAs(failure);
        node.Disposals.ShouldBe(1);
        node.Completion.IsCompleted.ShouldBeTrue();
        await provider.DisposeAsync();
        node.Disposals.ShouldBe(1);
        node.Starts.ShouldBe(0);
    }

    [Fact]
    public async Task A_factory_cannot_transfer_an_instance_from_another_application()
    {
        var node = new Probe();
        await using var ownerProvider = new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("original.node"), _ => node).BuildServiceProvider();
        await using var owner = await ownerProvider.BuildApplicationAsync();
        var services = new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("main.node"), _ => node);
        await using var provider = services.BuildServiceProvider();
        await Should.ThrowAsync<ArgumentException>(() => provider.BuildApplicationAsync());
        node.Disposals.ShouldBe(0);
        await owner.DisposeAsync();
        node.Disposals.ShouldBe(1);
        await ownerProvider.DisposeAsync();
        node.Disposals.ShouldBe(1);
    }

    [Fact]
    public void Duplicate_factory_addresses_are_rejected_without_overwriting_the_first()
    {
        var services = new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("main.node"), _ => new Probe());
        var count = services.Count;
        Should.Throw<ArgumentException>(() => services.AddComponentFactory(ComponentAddress.Parse("main.node"), _ => new Probe()));
        services.Count.ShouldBe(count);
    }

    [Fact]
    public async Task Built_application_can_be_disposed_without_ever_starting_and_keeps_host_services_alive()
    {
        var services = new ServiceCollection();
        services.AddSingleton<HostDependency>();
        HostDependency? dependency = null;
        var node = new Probe();
        services.AddComponentFactory(ComponentAddress.Parse("main.node"), provider =>
        {
            dependency = provider.GetRequiredService<HostDependency>();
            return node;
        });
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.DisposeAsync().AsTask().WaitAsync(Deadline);
        node.Starts.ShouldBe(0);
        node.Disposals.ShouldBe(1);
        dependency.ShouldNotBeNull();
        dependency.Disposals.ShouldBe(0);
        await provider.DisposeAsync();
        dependency.Disposals.ShouldBe(1);
        node.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Build_failure_and_provider_cleanup_failure_are_reported_to_the_host_separately()
    {
        var failure = new InvalidOperationException("link failed");
        var cleanup = new InvalidOperationException("cleanup failed");
        var node = new Probe(dispose: () => throw cleanup);
        var services = new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("main.node"), _ => node);
        services.AddSingleton(new LinkFactory(_ => throw failure));
        await using var provider = services.BuildServiceProvider();
        var error = await Should.ThrowAsync<AggregateException>(() => provider.BuildApplicationAsync().WaitAsync(Deadline));
        error.Flatten().InnerExceptions.ShouldBe([failure, cleanup]);
        node.Disposals.ShouldBe(1);
        (await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask())).GetBaseException().ShouldBeSameAs(cleanup);
        node.Starts.ShouldBe(0);
        node.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Keyed_links_reuse_instances_keep_host_dependencies_and_preserve_fanout_and_drain()
    {
        var created = 0;
        HostDependency? linkDependency = null;
        var services = new ServiceCollection();
        services.AddSingleton<HostDependency>();
        services.AddComponentFactory(ComponentAddress.Parse("main.echo"), _ =>
        {
            created++;
            return new Echo();
        });
        services.AddInputPort<Echo>(ComponentAddress.Parse("main.echo"), "Input", node => node.Input);
        services.AddOutputPort<Echo>(ComponentAddress.Parse("main.echo"), "Output", node => node.Output);
        foreach (var name in new[]
        {
            "first",
            "second"
        }

        )
        {
            services.AddComponentFactory(ComponentAddress.Parse($"other.{name}"), _ => new Sink());
            services.AddInputPort<Sink>(ComponentAddress.Parse($"other.{name}"), "Input", node => node.Input);
        }

        services.AddTransient<LinkFactory>(host => ports =>
        {
            linkDependency = host.GetRequiredService<HostDependency>();
            var echo = (Echo)ports.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.echo");
            ports.GetWorkflowServices("main").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.echo.Output").ShouldBeSameAs(echo.Output);
            ports.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.echo.Input").ShouldBeSameAs(echo.Input);
            foreach (var name in new[]
            {
                "first",
                "second"
            }

            )
                ports.Link(PortAddress.Parse("main.echo.Output"), PortAddress.Parse($"other.{name}.Input"));
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var dependency = provider.GetRequiredService<HostDependency>();
        await using var app = await provider.BuildApplicationAsync();
        var echo = (Echo)app.GetComponent(ComponentAddress.Parse("main.echo"));
        {
            created.ShouldBe(1);
            linkDependency.ShouldBeSameAs(dependency);
            echo.Disposals.ShouldBe(0);
            await app.StartAsync();
            var sinks = new[] { "first", "second" }.Select(name => (Sink)app.GetComponent(ComponentAddress.Parse($"other.{name}"))).ToArray();
            var message = FlowMessage.Create(JsonFlowValue.Null);
            (await app.GetTestInput("main.echo.Input").SendAsync(message)).ShouldBeTrue();
            await app.StopAsync().WaitAsync(Deadline);
            foreach (var sink in sinks) sink.Received.ShouldBe([message]);
            await app.DisposeAsync();
            echo.Disposals.ShouldBe(1);
            dependency.Disposals.ShouldBe(0);
        }

        await provider.DisposeAsync();
        dependency.Disposals.ShouldBe(1);
        echo.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Address_links_filter_each_route_independently()
    {
        var services = new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("main.echo"), _ => new Echo()).AddInputPort<Echo>(ComponentAddress.Parse("main.echo"), "Input", node => node.Input).AddOutputPort<Echo>(ComponentAddress.Parse("main.echo"), "Output", node => node.Output);
        foreach (var name in new[]
        {
            "matched",
            "other"
        }

        )
            services.AddComponentFactory(ComponentAddress.Parse($"sinks.{name}"), _ => new Sink()).AddInputPort<Sink>(ComponentAddress.Parse($"sinks.{name}"), "Input", node => node.Input);
        var matched = FlowMessage.Create(JsonFlowValue.Null);
        var unmatched = FlowMessage.Create(JsonFlowValue.Null);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link(PortAddress.Parse("main.echo.Output"), PortAddress.Parse("sinks.matched.Input"), message => ReferenceEquals(message, matched));
            ports.Link(PortAddress.Parse("main.echo.Output"), PortAddress.Parse("sinks.other.Input"));
        });
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        await app.GetTestInput("main.echo.Input").SendAsync(matched);
        await app.GetTestInput("main.echo.Input").SendAsync(unmatched);
        var selected = (Sink)app.GetComponent(ComponentAddress.Parse("sinks.matched"));
        var all = (Sink)app.GetComponent(ComponentAddress.Parse("sinks.other"));
        await app.StopAsync().WaitAsync(Deadline);
        selected.Received.ShouldBe([matched]);
        all.Received.ShouldBe([matched, unmatched]);
    }

    [Theory]
    [InlineData("main.missing.Output", "main.node.Input", "Output 'main.missing.Output'")]
    [InlineData("main.node.Output", "main.missing.Input", "Input 'main.missing.Input'")]
    public async Task Missing_link_address_reports_direction_and_rolls_back_build(string source, string target, string diagnostic)
    {
        var node = new Echo();
        var services = new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("main.node"), _ => node).AddInputPort<Echo>(ComponentAddress.Parse("main.node"), "Input", value => value.Input).AddOutputPort<Echo>(ComponentAddress.Parse("main.node"), "Output", value => value.Output);
        services.AddSingleton<LinkFactory>(ports => ports.Link(PortAddress.Parse(source), PortAddress.Parse(target)));
        await using var provider = services.BuildServiceProvider();
        var error = await Should.ThrowAsync<KeyNotFoundException>(() => provider.BuildApplicationAsync().WaitAsync(Deadline));
        error.Message.ShouldContain(diagnostic);
        node.Disposals.ShouldBe(1);
        await provider.DisposeAsync();
        node.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Native_input_aliases_keep_addresses_and_hosts_remain_isolated()
    {
        var services = new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("main.echo"), _ => new Echo()).AddInputPort<Echo>(ComponentAddress.Parse("main.echo"), "Input", node => node.Input).AddInputPort<Echo>(ComponentAddress.Parse("main.echo"), "Alias", node => node.Input).AddOutputPort<Echo>(ComponentAddress.Parse("main.echo"), "Output", node => node.Output).AddComponentFactory(ComponentAddress.Parse("other.sink"), _ => new Sink()).AddInputPort<Sink>(ComponentAddress.Parse("other.sink"), "Input", node => node.Input);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.echo.Alias").ShouldBeSameAs(ports.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.echo.Input"));
            var link = ports.Link(PortAddress.Parse("main.echo.Output"), PortAddress.Parse("other.sink.Input"));
            link.InputAddress.Value.ShouldBe("other.sink.Input");
            link.Input.ShouldBeSameAs(ports.GetWorkflowServices(link.InputAddress.ComponentAddress!.WorkflowName).GetRequiredKeyedService<ITargetBlock<FlowMessage>>(link.InputAddress.Value));
        });
        await using var provider = services.BuildServiceProvider();
        await using var first = await provider.BuildApplicationAsync();
        await using var otherProvider = services.BuildServiceProvider();
        await using var second = await otherProvider.BuildApplicationAsync();
        first.GetTestInput("main.echo.Input").ShouldNotBeSameAs(second.GetTestInput("main.echo.Input"));
        await first.StartAsync();
        await second.StartAsync();
        var message = FlowMessage.Create(JsonFlowValue.Null);
        (await first.GetTestInput("main.echo.Alias").SendAsync(message)).ShouldBeTrue();
        var firstSink = (Sink)first.GetComponent(ComponentAddress.Parse("other.sink"));
        var secondSink = (Sink)second.GetComponent(ComponentAddress.Parse("other.sink"));
        var firstEcho = (Echo)first.GetComponent(ComponentAddress.Parse("main.echo"));
        await first.StopAsync().WaitAsync(Deadline);
        await second.StopAsync().WaitAsync(Deadline);
        firstSink.Received.ShouldBe([message]);
        secondSink.Received.ShouldBeEmpty();
        (await firstEcho.Input.SendAsync(message)).ShouldBeFalse();
    }

    [Fact]
    public async Task Link_registration_rejects_foreign_native_input_even_with_the_same_address()
    {
        var services = new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("main.echo"), _ => new Echo()).AddInputPort<Echo>(ComponentAddress.Parse("main.echo"), "Input", node => node.Input).AddOutputPort<Echo>(ComponentAddress.Parse("main.echo"), "Output", node => node.Output);
        await using var provider = services.BuildServiceProvider();
        await using var first = await provider.BuildApplicationAsync();
        await using var otherProvider = services.BuildServiceProvider();
        await using var second = await otherProvider.BuildApplicationAsync();
        using var mixed = new ServiceCollection().AddKeyedSingleton("main.echo.Output", first.GetOutput("main.echo.Output").Block).AddSingleton(first).AddKeyedSingleton("main.echo.Input", second.GetTestInput("main.echo.Input")).BuildServiceProvider();
        Should.Throw<ArgumentException>(() => mixed.Link(PortAddress.Parse("main.echo.Output"), PortAddress.Parse("main.echo.Input"))).Message.ShouldContain("does not belong");
        first.GetOutput("main.echo.Output").Links.ShouldBeEmpty();
    }

    [Fact]
    public void Application_has_no_manual_input_lookup_or_send_api()
    {
        typeof(FlowApplication).GetMethods().Select(method => method.Name).ShouldNotContain("SendAsync");
        typeof(FlowApplication).GetMethods().Select(method => method.Name).ShouldNotContain("GetInput");
        typeof(FlowWorkflow).GetMethods().Select(method => method.Name).ShouldNotContain("GetInput");
        typeof(FlowApplicationExtensions).GetMethods().Select(method => method.Name).ShouldNotContain("GetInput");
        typeof(FlowWorkflow).GetMethods().Select(method => method.Name).ShouldNotContain("GetOutput");
        typeof(FlowApplicationExtensions).GetMethods().Select(method => method.Name).ShouldNotContain("GetOutput");
        typeof(FlowApplication).Assembly.GetType("FluxFlow.Application.Execution.FlowOutputEndpoint").ShouldBeNull();
    }

    private sealed class Echo : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public Echo()
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
            Lifetime.Observe();
        }

        public int Disposals { get; private set; }

        private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message) => Task.FromResult<IEnumerable<FlowMessage>>([message]);
        protected override ValueTask OnDisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Sink : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public Sink()
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
            Lifetime.Observe();
        }

        public List<FlowMessage> Received { get; } = [];

        private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
        {
            Received.Add(message);
            return Task.FromResult<IEnumerable<FlowMessage>>([]);
        }
    }

    private sealed class HostDependency : IDisposable
    {
        public int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    private sealed class Probe(Action? start = null, Action? dispose = null) : IFlowSource
    {
        private readonly ActionBlock<FlowMessage> _input = new(_ =>
        {
        });
        public ITargetBlock<FlowMessage> Input => _input;
        public int Starts { get; private set; }
        public int Disposals { get; private set; }
        public Task Completion => _input.Completion;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            Starts++;
            start?.Invoke();
            return Task.CompletedTask;
        }

        public void Complete() => _input.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)_input).Fault(error);
        public async ValueTask DisposeAsync()
        {
            Disposals++;
            Complete();
            await Completion;
            dispose?.Invoke();
        }
    }
}
