using FluxFlow.Core.Application;
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
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["workflows:main:node:Type"] = "probe"
        }).Build();
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
        await services.AddFluxFlowApplicationAsync(configuration);
        nodes.ShouldBeEmpty();
        await using var provider = services.BuildServiceProvider();
        nodes.ShouldBeEmpty();
        await using var app = await provider.BuildApplicationAsync();
        nodes.Count.ShouldBe(1);
        app.GetComponent("main.node").ShouldBeSameAs(nodes[0]);
        provider.GetRequiredKeyedService<IFlowNode>("main.node").ShouldBeSameAs(nodes[0]);
        await app.DisposeAsync();
        nodes[0].Disposed.ShouldBeFalse();
        await provider.DisposeAsync();
        nodes.All(node => node.Disposed).ShouldBeTrue();
    }

    [Fact]
    public async Task Factory_failure_stops_nodes_and_leaves_disposal_to_the_provider()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["workflows:main:first:Type"] = "probe",
            ["workflows:main:second:Type"] = "failure"
        }).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var failure = new InvalidOperationException("construction failed");
        Probe? first = null;
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ComponentRegistration>("probe", (registrations, section) =>
            registrations.AddComponentFactory(section.GetComponentAddress(), _ => first = new Probe()));
        services.AddKeyedSingleton<ComponentRegistration>("failure", (registrations, section) =>
            registrations.AddComponentFactory(section.GetComponentAddress(), _ => throw failure));
        await services.AddFluxFlowApplicationAsync(configuration);
        first.ShouldBeNull();
        await using var provider = services.BuildServiceProvider();
        var error = await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync());
        error.ShouldBeSameAs(failure);
        first.ShouldNotBeNull().Disposed.ShouldBeFalse();
        first.Completion.IsCompleted.ShouldBeTrue();
        await provider.DisposeAsync();
        first.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Missing_factory_still_rejects_registration_without_changing_host_services()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["workflows:main:node:Type"] = "empty"
        }).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ComponentRegistration>("empty", (_, _) => { });
        var before = services.ToArray();
        var error = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await services.AddFluxFlowApplicationAsync(configuration));
        error.Message.ShouldContain("Registration did not provide a factory");
        services.ToArray().ShouldBe(before);
    }

    private sealed class Probe : FlowTransformNode
    {
        public bool Disposed { get; private set; }
        protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
            => Task.FromResult<IEnumerable<FlowMessage>>([]);
        protected override ValueTask OnDisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
