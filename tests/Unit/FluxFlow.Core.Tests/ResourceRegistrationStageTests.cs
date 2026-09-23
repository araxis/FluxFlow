using FluxFlow.Core.Application;
using FluxFlow.Core.Nodes;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class ResourceRegistrationStageTests
{
    [Fact]
    public async Task Registration_is_transactional_and_resources_use_the_host_provider()
    {
        var services = new ServiceCollection();
        var registered = 0;
        var created = 0;
        IServiceCollection? captured = null;
        services.AddSingleton<Host>();
        services.AddApplicationResources(resources =>
        {
            registered++;
            captured = resources;
            resources.AddKeyedSingleton<Resource>("first", (_, _) => { created++; return new Resource(); });
        });
        registered.ShouldBe(1);
        created.ShouldBe(0);
        Should.Throw<InvalidOperationException>(() => captured!.AddSingleton(new object()));
        // A failed registration cannot discard the previously registered factory.
        var before = services.ToArray();
        Should.Throw<InvalidOperationException>(() => services.AddApplicationResources(resources =>
        {
            resources.Clear();
            throw new InvalidOperationException("bad registration");
        }));
        services.ToArray().ShouldBe(before);
        services.AddApplicationResources(resources =>
            resources.AddKeyedSingleton<Resource>("second", (_, _) => { created++; return new Resource(); }));
        var nodes = new List<Probe>();
        services.AddComponentFactory("main.node", provider =>
        {
            var node = new Probe(provider.GetRequiredKeyedService<Resource>("first"),
                provider.GetRequiredKeyedService<Resource>("second"), provider.GetRequiredService<Host>());
            nodes.Add(node);
            return node;
        });
        await using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var host = root.GetRequiredService<Host>();
        var resolved = root.GetRequiredKeyedService<IFlowNode>("main.node");
        root.GetRequiredKeyedService<IFlowNode>("main.node").ShouldBeSameAs(resolved);
        await using var app = await root.BuildApplicationAsync();
        created.ShouldBe(2);
        registered.ShouldBe(1);
        nodes[0].Host.ShouldBeSameAs(host);
        nodes.Count.ShouldBe(1);
        app.GetComponent("main.node").ShouldBeSameAs(resolved);
        await app.DisposeAsync();
        nodes[0].First.ShouldBeSameAs(root.GetRequiredKeyedService<Resource>("first"));
        nodes[0].First.Disposed.ShouldBeFalse();
        nodes[0].Second.Disposed.ShouldBeFalse();
        host.Disposed.ShouldBeFalse();
        await root.DisposeAsync();
        host.Disposed.ShouldBeTrue();
        nodes[0].First.Disposed.ShouldBeTrue();
        nodes[0].Second.Disposed.ShouldBeTrue();
    }

    private sealed class Host : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }
    private sealed class Resource : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }
    private sealed class Probe(Resource first, Resource second, Host host) : FlowTransformNode
    {
        public Resource First => first;
        public Resource Second => second;
        public Host Host => host;
        protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
            => Task.FromResult<IEnumerable<FlowMessage>>([]);
        protected override ValueTask OnDisposeAsync()
        {
            first.Disposed.ShouldBeFalse();
            second.Disposed.ShouldBeFalse();
            host.Disposed.ShouldBeFalse();
            return ValueTask.CompletedTask;
        }
    }
}
