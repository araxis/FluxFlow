using System.Threading.Tasks.Dataflow;
using FluxFlow.Application.Addressing;
using FluxFlow.Application;
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
            resources.AddKeyedSingleton<Resource>("first", (_, _) =>
            {
                created++;
                return new Resource();
            });
        });
        registered.ShouldBe(1);
        created.ShouldBe(0);
        Should.Throw<InvalidOperationException>(() => captured!.AddSingleton(new object ()));
        // A failed registration cannot discard the previously registered factory.
        var before = services.ToArray();
        Should.Throw<InvalidOperationException>(() => services.AddApplicationResources(resources =>
        {
            resources.Clear();
            throw new InvalidOperationException("bad registration");
        }));
        services.ToArray().ShouldBe(before);
        services.AddApplicationResources(resources => resources.AddKeyedSingleton<Resource>("second", (_, _) =>
        {
            created++;
            return new Resource();
        }));
        var nodes = new List<Probe>();
        services.AddComponentFactory(ComponentAddress.Parse("main.node"), provider =>
        {
            var node = new Probe(provider.GetRequiredKeyedService<Resource>("first"), provider.GetRequiredKeyedService<Resource>("second"), provider.GetRequiredService<Host>());
            nodes.Add(node);
            return node;
        });
        await using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var host = root.GetRequiredService<Host>();
        var resolved = root.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.node");
        root.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.node").ShouldBeSameAs(resolved);
        await using var app = await root.BuildApplicationAsync();
        created.ShouldBe(2);
        registered.ShouldBe(1);
        nodes[0].Host.ShouldBeSameAs(host);
        nodes.Count.ShouldBe(1);
        app.GetComponent(ComponentAddress.Parse("main.node")).ShouldBeSameAs(resolved);
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

    private sealed class Probe : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public Probe(Resource first, Resource second, Host host)
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
            this.first = first;
            this.second = second;
            this.host = host;
            Lifetime.Observe();
        }

        private readonly Host host;
        private readonly Resource second;
        private readonly Resource first;
        public Resource First => first;
        public Resource Second => second;
        public Host Host => host;

        private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message) => Task.FromResult<IEnumerable<FlowMessage>>([]);
        protected override ValueTask OnDisposeAsync()
        {
            first.Disposed.ShouldBeFalse();
            second.Disposed.ShouldBeFalse();
            host.Disposed.ShouldBeFalse();
            return ValueTask.CompletedTask;
        }
    }
}
