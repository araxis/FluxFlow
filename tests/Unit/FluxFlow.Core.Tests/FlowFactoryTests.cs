using FluxFlow.Core.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using FluxFlow.Core.Execution;
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
    public async Task Build_requires_a_registered_application()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        await Should.ThrowAsync<InvalidOperationException>(async () => await provider.BuildApplicationAsync());
    }

    [Fact]
    public async Task Default_build_preserves_registered_capacity_and_application_identity()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_ => new FlowApplication(17));
        await using var provider = services.BuildServiceProvider();
        var first = await provider.BuildApplicationAsync();
        (await provider.BuildApplicationAsync(17)).ShouldBeSameAs(first);
        (await provider.BuildApplicationAsync()).ShouldBeSameAs(first);
        await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync(128));
    }

    [Fact]
    public async Task Default_build_preserves_explicit_build_capacity()
    {
        var services = new ServiceCollection().AddFlowWorkflow("empty");
        await using var provider = services.BuildServiceProvider();
        var first = await provider.BuildApplicationAsync(19);
        (await provider.BuildApplicationAsync()).ShouldBeSameAs(first);
    }

    [Fact]
    public async Task Build_composes_once_and_start_only_starts_sources()
    {
        var order = new List<string>();
        var services = new ServiceCollection().AddFlowWorkflow("empty");
        foreach (var name in new[] { "first", "second" })
        {
            var address = $"a.{name}";
            services.AddComponentFactory(address, provider =>
            {
                order.Add(name);
                return new Probe(() => order.Add($"start-{name}"));
            });
            services.AddInputPort<Probe>(address, "Input", node => node.Input);
        }
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("a.first.Input").ShouldNotBeNull();
            ports.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("a.second.Input").ShouldNotBeNull();
            order.Add("links");
        });
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredKeyedService<ComponentFactory>("a.first").ShouldNotBeNull();
        order.ShouldBeEmpty();
        await using var app = await provider.BuildApplicationAsync();
        order.ShouldBe(["first", "second", "links"]);
        app.Workflows.Count.ShouldBe(2);
        var starting = app.StartAsync();
        app.StartAsync().ShouldBeSameAs(starting);
        await starting.WaitAsync(Deadline);
        order.ShouldBe(["first", "second", "links", "start-first", "start-second"]);
        app.Workflows.Keys.ShouldContain("empty");
        app.GetComponent("a.first").ShouldBeOfType<Probe>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_factory_or_link_stops_components_before_provider_cleanup(bool failLink)
    {
        var node = new Probe();
        var failure = new InvalidOperationException("factory failed");
        var services = new ServiceCollection().AddComponentFactory("main.first", _ => node);
        services.AddComponentFactory("main.second", _ => failLink ? new Probe() : throw failure);
        services.AddSingleton(new LinkFactory(_ => throw failure));
        await using var provider = services.BuildServiceProvider();
        (await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync().WaitAsync(Deadline)))
            .ShouldBeSameAs(failure);
        node.Starts.ShouldBe(0);
        node.Disposals.ShouldBe(0);
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
        var services = new ServiceCollection().AddComponentFactory("main.first", _ =>
        {
            cancellation.Cancel();
            return node;
        });
        services.AddComponentFactory("main.second", _ => { secondCreated = true; return new Probe(); });
        services.AddSingleton<LinkFactory>(_ => { linked = true; });
        await using var provider = services.BuildServiceProvider();
        await Should.ThrowAsync<OperationCanceledException>(() => provider.BuildApplicationAsync(cancellationToken: cancellation.Token).WaitAsync(Deadline));
        node.Starts.ShouldBe(0);
        node.Disposals.ShouldBe(0);
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
        var services = new ServiceCollection().AddComponentFactory("main.node", _ => node);
        services.AddInputPort<Probe>("main.node", "Input", _ => throw failure);
        await using var provider = services.BuildServiceProvider();
        (await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync())).ShouldBeSameAs(failure);
        node.Disposals.ShouldBe(0);
        node.Completion.IsCompleted.ShouldBeTrue();
        await provider.DisposeAsync();
        node.Disposals.ShouldBe(1);
        node.Starts.ShouldBe(0);
    }

    [Fact]
    public async Task A_factory_cannot_transfer_an_instance_from_another_application()
    {
        var node = new Probe();
        await using var ownerProvider = new ServiceCollection().AddComponentFactory("original.node", _ => node).BuildServiceProvider();
        await using var owner = await ownerProvider.BuildApplicationAsync();
        var services = new ServiceCollection().AddComponentFactory("main.node", _ => node);
        await using var provider = services.BuildServiceProvider();
        await Should.ThrowAsync<ArgumentException>(() => provider.BuildApplicationAsync());
        node.Disposals.ShouldBe(0);
        await owner.DisposeAsync();
        node.Disposals.ShouldBe(0);
        await ownerProvider.DisposeAsync();
        node.Disposals.ShouldBe(1);
    }

    [Fact]
    public void Duplicate_factory_addresses_are_rejected_without_overwriting_the_first()
    {
        var services = new ServiceCollection().AddComponentFactory("main.node", _ => new Probe());
        var count = services.Count;
        Should.Throw<ArgumentException>(() => services.AddComponentFactory("main.node", _ => new Probe()));
        services.Count.ShouldBe(count);
    }

    [Fact]
    public async Task Built_application_can_be_disposed_without_ever_starting_and_keeps_host_services_alive()
    {
        var services = new ServiceCollection();
        services.AddSingleton<HostDependency>();
        HostDependency? dependency = null;
        var node = new Probe();
        services.AddComponentFactory("main.node", provider =>
        {
            dependency = provider.GetRequiredService<HostDependency>();
            return node;
        });
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.DisposeAsync().AsTask().WaitAsync(Deadline);
        node.Starts.ShouldBe(0);
        node.Disposals.ShouldBe(0);
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
        var services = new ServiceCollection().AddComponentFactory("main.node", _ => node);
        services.AddSingleton(new LinkFactory(_ => throw failure));
        await using var provider = services.BuildServiceProvider();
        (await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync().WaitAsync(Deadline)))
            .ShouldBeSameAs(failure);
        node.Disposals.ShouldBe(0);
        (await Should.ThrowAsync<InvalidOperationException>(() => provider.DisposeAsync().AsTask()))
            .ShouldBeSameAs(cleanup);
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
        services.AddComponentFactory("main.echo", _ => { created++; return new Echo(); });
        services.AddInputPort<Echo>("main.echo", "Input", node => node.Input);
        services.AddOutputPort<Echo>("main.echo", "Output", node => node.Output);
        foreach (var name in new[] { "first", "second" })
        {
            services.AddComponentFactory($"other.{name}", _ => new Sink());
            services.AddInputPort<Sink>($"other.{name}", "Input", node => node.Input);
        }
        services.AddTransient<LinkFactory>(host => ports =>
        {
            linkDependency = host.GetRequiredService<HostDependency>();
            var echo = (Echo)ports.GetRequiredKeyedService<IFlowNode>("main.echo");
            ports.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.echo.Output").ShouldBeSameAs(echo.Output);
            ports.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.echo.Input").ShouldBeSameAs(echo.Input);
            foreach (var name in new[] { "first", "second" })
                ports.Link("main.echo.Output", $"other.{name}.Input");
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var dependency = provider.GetRequiredService<HostDependency>();
        await using var app = await provider.BuildApplicationAsync();
        var echo = (Echo)app.GetComponent("main.echo");
        {
            created.ShouldBe(1);
            linkDependency.ShouldBeSameAs(dependency);
            echo.Disposals.ShouldBe(0);
            await app.StartAsync();
            var message = FlowMessage.Create(JsonFlowValue.Null);
            (await app.GetTestInput("main.echo.Input").SendAsync(message)).ShouldBeTrue();
            await app.StopAsync().WaitAsync(Deadline);
            foreach (var name in new[] { "first", "second" })
                ((Sink)app.GetComponent($"other.{name}")).Received.ShouldBe([message]);
            await app.DisposeAsync();
            echo.Disposals.ShouldBe(0);
            dependency.Disposals.ShouldBe(0);
        }
        await provider.DisposeAsync();
        dependency.Disposals.ShouldBe(1);
        echo.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Address_links_keep_conditions_and_fallback()
    {
        var services = new ServiceCollection()
            .AddComponentFactory("main.echo", _ => new Echo())
            .AddInputPort<Echo>("main.echo", "Input", node => node.Input)
            .AddOutputPort<Echo>("main.echo", "Output", node => node.Output);
        foreach (var name in new[] { "matched", "other" })
            services.AddComponentFactory($"sinks.{name}", _ => new Sink())
                .AddInputPort<Sink>($"sinks.{name}", "Input", node => node.Input);
        var matched = FlowMessage.Create(JsonFlowValue.Null);
        var unmatched = FlowMessage.Create(JsonFlowValue.Null);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.echo.Output", "sinks.matched.Input", message => ReferenceEquals(message, matched));
            ports.LinkFallback("main.echo.Output", "sinks.other.Input");
        });
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        await app.GetTestInput("main.echo.Input").SendAsync(matched);
        await app.GetTestInput("main.echo.Input").SendAsync(unmatched);
        await app.StopAsync().WaitAsync(Deadline);
        ((Sink)app.GetComponent("sinks.matched")).Received.ShouldBe([matched]);
        ((Sink)app.GetComponent("sinks.other")).Received.ShouldBe([unmatched]);
    }

    [Theory]
    [InlineData("missing.Output", "main.node.Input", "Output 'missing.Output'")]
    [InlineData("main.node.Output", "missing.Input", "Input 'missing.Input'")]
    public async Task Missing_link_address_reports_direction_and_rolls_back_build(string source, string target, string diagnostic)
    {
        var node = new Echo();
        var services = new ServiceCollection()
            .AddComponentFactory("main.node", _ => node)
            .AddInputPort<Echo>("main.node", "Input", value => value.Input)
            .AddOutputPort<Echo>("main.node", "Output", value => value.Output);
        services.AddSingleton<LinkFactory>(ports => ports.Link(source, target));
        await using var provider = services.BuildServiceProvider();
        var error = await Should.ThrowAsync<KeyNotFoundException>(() => provider.BuildApplicationAsync().WaitAsync(Deadline));
        error.Message.ShouldContain(diagnostic);
        node.Disposals.ShouldBe(0);
        await provider.DisposeAsync();
        node.Disposals.ShouldBe(1);
    }


    [Fact]
    public async Task Native_input_aliases_keep_addresses_and_hosts_remain_isolated()
    {
        var services = new ServiceCollection()
            .AddComponentFactory("main.echo", _ => new Echo())
            .AddInputPort<Echo>("main.echo", "Input", node => node.Input)
            .AddInputPort<Echo>("main.echo", "Alias", node => node.Input)
            .AddOutputPort<Echo>("main.echo", "Output", node => node.Output)
            .AddComponentFactory("other.sink", _ => new Sink())
            .AddInputPort<Sink>("other.sink", "Input", node => node.Input);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.echo.Alias")
                .ShouldBeSameAs(ports.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.echo.Input"));
            var link = ports.Link("main.echo.Output", "other.sink.Input");
            link.InputAddress.ShouldBe("other.sink.Input");
            link.Input.ShouldBeSameAs(ports.GetRequiredKeyedService<ITargetBlock<FlowMessage>>(link.InputAddress));
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
        await first.StopAsync().WaitAsync(Deadline);
        await second.StopAsync().WaitAsync(Deadline);
        ((Sink)first.GetComponent("other.sink")).Received.ShouldBe([message]);
        ((Sink)second.GetComponent("other.sink")).Received.ShouldBeEmpty();
        (await ((Echo)first.GetComponent("main.echo")).Input.SendAsync(message)).ShouldBeFalse();
    }

    [Fact]
    public async Task Link_delegate_rejects_foreign_native_input_even_with_the_same_address()
    {
        var services = new ServiceCollection()
            .AddComponentFactory("main.echo", _ => new Echo())
            .AddInputPort<Echo>("main.echo", "Input", node => node.Input)
            .AddOutputPort<Echo>("main.echo", "Output", node => node.Output);
        await using var provider = services.BuildServiceProvider();
        await using var first = await provider.BuildApplicationAsync();
        await using var otherProvider = services.BuildServiceProvider();
        await using var second = await otherProvider.BuildApplicationAsync();
        using var mixed = new ServiceCollection()
            .AddKeyedSingleton("main.echo.Output", first.GetOutput("main.echo.Output").Block)
            .AddKeyedSingleton("main.echo.Output", provider.GetRequiredKeyedService<Action<FluxFlow.Core.Execution.FlowLink>>("main.echo.Output"))
            .AddKeyedSingleton("main.echo.Input", second.GetTestInput("main.echo.Input"))
            .BuildServiceProvider();
        Should.Throw<ArgumentException>(() => mixed.Link("main.echo.Output", "main.echo.Input"))
            .Message.ShouldContain("does not belong");
        first.GetOutput("main.echo.Output").Links.ShouldBeEmpty();
    }

    [Fact]
    public void Application_has_no_manual_input_lookup_or_send_api()
    {
        typeof(FlowApplication).GetMethods().Select(method => method.Name).ShouldNotContain("SendAsync");
        typeof(FlowApplication).GetMethods().Select(method => method.Name).ShouldNotContain("GetInput");
        typeof(FlowWorkflow).GetMethods().Select(method => method.Name).ShouldNotContain("GetInput");
        typeof(FlowExtensions).GetMethods().Select(method => method.Name).ShouldNotContain("GetInput");
        typeof(FlowWorkflow).GetMethods().Select(method => method.Name).ShouldNotContain("GetOutput");
        typeof(FlowExtensions).GetMethods().Select(method => method.Name).ShouldNotContain("GetOutput");
        typeof(FlowApplication).Assembly.GetType("FluxFlow.Core.Execution.FlowOutputEndpoint").ShouldBeNull();
    }

    private sealed class Echo : FlowTransformNode
    {
        public int Disposals { get; private set; }
        protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
            => Task.FromResult<IEnumerable<FlowMessage>>([message]);
        protected override ValueTask OnDisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class Sink : FlowTransformNode
    {
        public List<FlowMessage> Received { get; } = [];
        protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
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
        private readonly ActionBlock<FlowMessage> _input = new(_ => { });
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
