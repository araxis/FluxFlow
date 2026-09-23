using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using FluxFlow.Core.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Core.Tests;

public sealed class NativeDiOwnershipTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_component_cannot_be_captured_again_as_a_disposable_port(bool output, bool resolveBeforeBuild)
    {
        var node = new AliasedNode();
        var services = new ServiceCollection();
        services.AddComponentFactory("main.node", _ => node);
        if (output) services.AddOutputPort<AliasedNode>("main.node", "Port", value => value);
        else services.AddInputPort<AliasedNode>("main.node", "Port", value => value);
        await using var provider = services.BuildServiceProvider();
        InvalidOperationException failure;
        if (resolveBeforeBuild)
            failure = Should.Throw<InvalidOperationException>(() => provider.GetRequiredKeyedService(
                output ? typeof(ISourceBlock<FlowMessage>) : typeof(ITargetBlock<FlowMessage>), "main.node.Port"));
        else failure = await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync());
        failure.Message.ShouldContain("main.node.Port");
        failure.Message.ShouldContain("non-disposable");
        node.Disposals.ShouldBe(0);
        await provider.DisposeAsync();
        node.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Synchronous_disposable_port_remains_owned_only_by_its_component()
    {
        var node = new PortOwner();
        var services = new ServiceCollection().AddComponentFactory("main.node", _ => node)
            .AddInputPort<PortOwner>("main.node", "Input", value => value.Input);
        await using var provider = services.BuildServiceProvider();
        (await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync())).Message.ShouldContain("non-disposable");
        node.Input.Disposals.ShouldBe(0);
        await provider.DisposeAsync();
        node.Input.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task A_second_key_cannot_capture_disposal_of_an_already_resolved_component()
    {
        var node = new Processor();
        var services = new ServiceCollection();
        services.AddComponentFactory("main.first", _ => node);
        services.AddComponentFactory("main.second", _ => node);
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredKeyedService<IFlowNode>("main.first").ShouldBeSameAs(node);
        Should.Throw<ArgumentException>(() => provider.GetRequiredKeyedService<IFlowNode>("main.second"));
        node.Disposals.ShouldBe(0);
        await provider.DisposeAsync();
        node.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Application_and_direct_port_resolution_use_the_same_DI_owned_component()
    {
        var created = 0;
        var inputsSelected = 0;
        var outputsSelected = 0;
        var services = new ServiceCollection();
        services.AddComponentFactory("main.node", _ => { created++; return new Processor(); })
            .AddInputPort<Processor>("main.node", "Input", node => { inputsSelected++; return node.Input; })
            .AddOutputPort<Processor>("main.node", "Output", node => { outputsSelected++; return node.Output; });
        await using var provider = services.BuildServiceProvider();
        created.ShouldBe(0);
        var output = provider.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.node.Output");
        var node = (Processor)provider.GetRequiredKeyedService<IFlowNode>("main.node");
        output.ShouldBeSameAs(node.Output);
        (output is IAsyncDisposable).ShouldBeFalse();
        provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Input").ShouldBeSameAs(node.Input);
        var app = await provider.BuildApplicationAsync();
        app.GetComponent("main.node").ShouldBeSameAs(node);
        created.ShouldBe(1);
        inputsSelected.ShouldBe(1);
        outputsSelected.ShouldBe(1);
        provider.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.node.Output").ShouldBeSameAs(output);
        app.Workflows["main"].Components["main.node"].ShouldBeSameAs(node);
        await app.DisposeAsync();
        node.Disposals.ShouldBe(0);
        await provider.DisposeAsync();
        node.Disposals.ShouldBe(1);
    }

    [Fact]
    public void Duplicate_port_addresses_are_rejected_without_replacing_the_original_registration()
    {
        var services = new ServiceCollection();
        services.AddComponentFactory("main.node", _ => new Processor())
            .AddInputPort<Processor>("main.node", "Input", node => node.Input)
            .AddOutputPort<Processor>("main.node", "Output", node => node.Output);
        var before = services.ToArray();
        Should.Throw<ArgumentException>(() => services.AddInputPort<Processor>("main.node", "Input", node => node.Input));
        Should.Throw<ArgumentException>(() => services.AddOutputPort<Processor>("main.node", "Output", node => node.Output));
        services.ToArray().ShouldBe(before);
    }

    [Fact]
    public async Task Links_use_the_host_provider_and_repeated_build_returns_its_single_application()
    {
        var marker = new object();
        var links = 0;
        var services = new ServiceCollection().AddSingleton(marker);
        services.AddComponentFactory("main.first", _ => new Processor())
            .AddOutputPort<Processor>("main.first", "Output", node => node.Output);
        services.AddComponentFactory("main.second", _ => new Processor())
            .AddInputPort<Processor>("main.second", "Input", node => node.Input);
        IServiceProvider? linkProvider = null;
        services.AddSingleton<LinkFactory>(provider =>
        {
            linkProvider = provider;
            provider.GetRequiredService<object>().ShouldBeSameAs(marker);
            provider.Link("main.first.Output", "main.second.Input");
            links++;
        });
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        provider.GetRequiredService<FlowApplication>().ShouldBeSameAs(app);
        (await provider.BuildApplicationAsync()).ShouldBeSameAs(app);
        links.ShouldBe(1);
        linkProvider.ShouldBeSameAs(provider);
        var route = app.Workflows["main"].Links.Single();
        route.Input.ShouldBeSameAs(provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.second.Input"));
        route.Output.ShouldBeSameAs(provider.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.first.Output"));
        await app.StartAsync();
        Should.Throw<InvalidOperationException>(() => provider.Link("main.first.Output", "main.second.Input"));
    }

    [Fact]
    public async Task Named_events_resolve_the_same_stamped_output_before_and_during_composition()
    {
        var selections = 0;
        var services = new ServiceCollection();
        services.AddComponentFactory("main.node", _ => new Processor())
            .AddEventPort<Processor>("main.node", "Diagnostics", node => { selections++; return node.Events; });
        await using var provider = services.BuildServiceProvider();
        var events = provider.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.node.Diagnostics");
        await using var app = await provider.BuildApplicationAsync();
        provider.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.node.Diagnostics").ShouldBeSameAs(events);
        app.Workflows["main"].EventBridges.Single().Output.ShouldBeSameAs(events);
        selections.ShouldBe(1);
        app.Workflows["main"].EventBridges.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_keyed_resolution_and_native_ports_share_one_instance()
    {
        var created = 0;
        var services = new ServiceCollection();
        services.AddKeyedSingleton<Node>("main.node", (_, _) =>
        {
            Interlocked.Increment(ref created);
            return new Node();
        });
        services.AddKeyedSingleton<ITargetBlock<int>>("main.node.Input",
            (provider, _) => provider.GetRequiredKeyedService<Node>("main.node").Block);
        services.AddKeyedSingleton<ISourceBlock<int>>("main.node.Output",
            (provider, _) => provider.GetRequiredKeyedService<Node>("main.node").Block);
        await using var provider = services.BuildServiceProvider();
        created.ShouldBe(0);
        var nodes = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
            provider.GetRequiredKeyedService<Node>("main.node"))));
        created.ShouldBe(1);
        nodes.All(node => ReferenceEquals(node, nodes[0])).ShouldBeTrue();
        provider.GetRequiredKeyedService<ITargetBlock<int>>("main.node.Input").ShouldBeSameAs(nodes[0].Block);
        provider.GetRequiredKeyedService<ISourceBlock<int>>("main.node.Output").ShouldBeSameAs(nodes[0].Block);
        await provider.DisposeAsync();
        nodes[0].Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Disposable_alias_factories_are_tracked_separately_by_native_DI()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<Node>("node", (_, _) => new Node());
        services.AddKeyedSingleton<IAsyncDisposable>("alias",
            (provider, _) => provider.GetRequiredKeyedService<Node>("node"));
        var provider = services.BuildServiceProvider();
        var node = provider.GetRequiredKeyedService<Node>("node");
        provider.GetRequiredKeyedService<IAsyncDisposable>("alias").ShouldBeSameAs(node);
        await provider.DisposeAsync();
        node.Disposals.ShouldBe(2);
    }

    [Fact]
    public async Task Native_provider_disposal_stops_at_the_first_disposal_failure()
    {
        var failure = new InvalidOperationException("cleanup");
        var services = new ServiceCollection();
        services.AddKeyedSingleton<Node>("first", (_, _) => new Node());
        services.AddKeyedSingleton<Node>("last", (_, _) => new Node(failure));
        var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredKeyedService<Node>("first");
        var last = provider.GetRequiredKeyedService<Node>("last");
        var error = await Should.ThrowAsync<InvalidOperationException>(async () => await provider.DisposeAsync());
        error.ShouldBeSameAs(failure);
        last.Disposals.ShouldBe(1);
        first.Disposals.ShouldBe(0);
        // Provider disposal cannot be retried to finish the remaining services.
        await provider.DisposeAsync();
        first.Disposals.ShouldBe(0);
        await first.DisposeAsync();
    }

    private class NativePort : IPropagatorBlock<FlowMessage, FlowMessage>
    {
        private readonly BufferBlock<FlowMessage> _block = new();
        public Task Completion => _block.Completion;
        public void Complete() => _block.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)_block).Fault(error);
        public IDisposable LinkTo(ITargetBlock<FlowMessage> target, DataflowLinkOptions options) => _block.LinkTo(target, options);
        public FlowMessage? ConsumeMessage(DataflowMessageHeader header, ITargetBlock<FlowMessage> target, out bool consumed)
            => ((ISourceBlock<FlowMessage>)_block).ConsumeMessage(header, target, out consumed);
        public bool ReserveMessage(DataflowMessageHeader header, ITargetBlock<FlowMessage> target)
            => ((ISourceBlock<FlowMessage>)_block).ReserveMessage(header, target);
        public void ReleaseReservation(DataflowMessageHeader header, ITargetBlock<FlowMessage> target)
            => ((ISourceBlock<FlowMessage>)_block).ReleaseReservation(header, target);
        public DataflowMessageStatus OfferMessage(DataflowMessageHeader header, FlowMessage value, ISourceBlock<FlowMessage>? source, bool consumeToAccept)
            => ((ITargetBlock<FlowMessage>)_block).OfferMessage(header, value, source, consumeToAccept);
    }

    private sealed class AliasedNode : NativePort, IFlowNode
    {
        public int Disposals { get; private set; }
        public ValueTask DisposeAsync() { Disposals++; Complete(); return ValueTask.CompletedTask; }
    }

    private sealed class DisposableInput : NativePort, IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose() { Disposals++; Complete(); }
    }

    private sealed class PortOwner : IFlowNode
    {
        public DisposableInput Input { get; } = new();
        public Task Completion => Input.Completion;
        public void Complete() => Input.Complete();
        public void Fault(Exception error) => Input.Fault(error);
        public ValueTask DisposeAsync() { Input.Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class Node(Exception? failure = null) : IAsyncDisposable
    {
        public BufferBlock<int> Block { get; } = new();
        public int Disposals { get; private set; }
        public ValueTask DisposeAsync()
        {
            Disposals++;
            Block.Complete();
            return failure is null ? ValueTask.CompletedTask : ValueTask.FromException(failure);
        }
    }

    private sealed class Processor : FlowTransformNode
    {
        public int Disposals { get; private set; }
        protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
            => Task.FromResult<IEnumerable<FlowMessage>>([]);
        protected override ValueTask OnDisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
