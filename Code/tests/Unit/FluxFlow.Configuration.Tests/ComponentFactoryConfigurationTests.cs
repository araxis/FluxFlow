using System.Threading.Tasks.Dataflow;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using FluxFlow.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;
public sealed class ComponentFactoryConfigurationTests
{
    [Fact]
    public async Task Configuration_defers_construction_to_build()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["workflows:main:node:Type"] = "probe" }).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var nodes = new List<Probe>();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ComponentRegistration>("probe", (registrations, section) =>
        {
            Probe Create()
            {
                var node = new Probe();
                nodes.Add(node);
                return node;
            }

            registrations.AddComponentFactory(section.GetComponentAddress(), _ => Create());
        });
        services.AddFluxFlowApplication(configuration).ShouldBeSameAs(services);
        nodes.ShouldBeEmpty();
        await using var provider = services.BuildServiceProvider();
        nodes.ShouldBeEmpty();
        await using var app = await provider.BuildApplicationAsync();
        nodes.Count.ShouldBe(1);
        app.GetComponent(ComponentAddress.Parse("main.node")).ShouldBeSameAs(nodes[0]);
        provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.node").ShouldBeSameAs(nodes[0]);
        await app.DisposeAsync();
        nodes[0].Disposed.ShouldBeTrue();
        await provider.DisposeAsync();
        nodes.All(node => node.Disposed).ShouldBeTrue();
    }

    [Fact]
    public async Task Factory_failure_stops_nodes_and_leaves_disposal_to_the_provider()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["workflows:main:first:Type"] = "probe", ["workflows:main:second:Type"] = "failure" }).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var failure = new InvalidOperationException("construction failed");
        Probe? first = null;
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ComponentRegistration>("probe", (registrations, section) => registrations.AddComponentFactory(section.GetComponentAddress(), _ => first = new Probe()));
        services.AddKeyedSingleton<ComponentRegistration>("failure", (registrations, section) => registrations.AddComponentFactory(section.GetComponentAddress(), _ => throw failure));
        services.AddFluxFlowApplication(configuration);
        first.ShouldBeNull();
        await using var provider = services.BuildServiceProvider();
        var error = await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync());
        error.ShouldBeSameAs(failure);
        first.ShouldNotBeNull().Disposed.ShouldBeTrue();
        first.Completion.IsCompleted.ShouldBeTrue();
        await provider.DisposeAsync();
        first.Disposed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_node_factory_rejects_registration_even_when_a_standalone_delegate_exists(bool registerDelegate)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["workflows:main:node:Type"] = "empty" }).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ComponentRegistration>("empty", (registrations, section) =>
        {
            if (registerDelegate)
                registrations.AddKeyedSingleton<ComponentFactory>(section.GetComponentAddress().Value, _ => throw new InvalidOperationException("Must not construct during registration."));
        });
        var before = services.ToArray();
        var error = Should.Throw<InvalidOperationException>(() => services.AddFluxFlowApplication(configuration));
        error.Message.ShouldContain("Registration did not provide a factory");
        services.ToArray().ShouldBe(before);
    }

    private sealed class Probe : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public Probe()
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

        public bool Disposed { get; private set; }

        private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message) => Task.FromResult<IEnumerable<FlowMessage>>([]);
        protected override ValueTask OnDisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
