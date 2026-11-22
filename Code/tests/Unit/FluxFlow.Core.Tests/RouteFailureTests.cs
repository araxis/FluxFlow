using FluxFlow.Application.Addressing;
using FluxFlow.Application;
using FluxFlow.Application.Execution;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed partial class InstanceFirstGraphTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Condition_failure_faults_graph_after_prior_native_offers(bool nativeSource)
    {
        var message = Value(13);
        var error = new InvalidOperationException("strict condition failure");
        var selected = new ProbeSink();
        var failing = new ProbeSink();
        var unvisited = new ProbeSink();
        var calls = 0;
        IFlowNode source = nativeSource ? new NativeSequenceSource([message]) : new SequenceSource([message]);
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", source);
        RegisterSink(services, "main.selected", selected);
        RegisterSink(services, "main.failing", failing);
        RegisterSink(services, "main.unvisited", unvisited);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.selected.Input"));
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.failing.Input"), _ => { calls++; throw error; });
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.unvisited.Input"), _ => { calls++; return true; });
        });
        await using var provider = services.BuildServiceProvider();
        var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();

        (await Should.ThrowAsync<InvalidOperationException>(() => appExecution.Completion.WaitAsync(TestDeadline))).ShouldBeSameAs(error);
        calls.ShouldBe(1);
        selected.Offers.ShouldBe(1);
        failing.Offers.ShouldBe(0);
        unvisited.Offers.ShouldBe(0);
        await Should.ThrowAsync<AggregateException>(() => appExecution.DisposeAsync().AsTask().WaitAsync(TestDeadline));
        (await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask()))
            .Flatten().InnerExceptions.ShouldContain(error);
    }

    [Fact]
    public async Task Link_preserves_native_blocks_addresses_and_condition()
    {
        var services = new ServiceCollection()
            .AddComponentFactory(ComponentAddress.Parse("main.source"), _ => new SequenceSource([]))
            .AddOutputPort<SequenceSource>(ComponentAddress.Parse("main.source"), "Output", node => node.Output)
            .AddComponentFactory(ComponentAddress.Parse("main.target"), _ => new BufferedSink())
            .AddInputPort<BufferedSink>(ComponentAddress.Parse("main.target"), "Input", node => node.Block);
        await using var ports = services.BuildServiceProvider();
        await using var appExecution = await ports.BuildApplicationAsync();
        var output = "main.source.Output";
        var input = "main.target.Input";
        Predicate<FlowMessage> condition = _ => true;
        var link = ports.Link(PortAddress.Parse(output), PortAddress.Parse(input), condition);
        link.Output.ShouldBeSameAs(ports.GetWorkflowServices("main").GetRequiredKeyedService<ISourceBlock<FlowMessage>>(output));
        link.OutputAddress.Value.ShouldBe(output);
        link.InputAddress.Value.ShouldBe(input);
        link.Input.ShouldBeSameAs(ports.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>(input));
        link.Condition.ShouldBeSameAs(condition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Component_lifetime_failure_faults_linked_graph(bool nativeSource)
    {
        var error = new InvalidOperationException("component lifetime failure");
        var source = new ManualRouteSource(nativeSource);
        var target = new ProbeSink();
        var sibling = new ProbeSink();
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", source);
        RegisterSink(services, "main.target", target);
        RegisterSink(services, "main.sibling", sibling);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.target.Input"));
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.sibling.Input"));
        });
        await using var provider = services.BuildServiceProvider();
        var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        target.Fault(error);

        (await Should.ThrowAsync<InvalidOperationException>(() => appExecution.Completion.WaitAsync(TestDeadline))).ShouldBeSameAs(error);
        (await Should.ThrowAsync<InvalidOperationException>(() => sibling.Completion.WaitAsync(TestDeadline))).ShouldBeSameAs(error);
        source.Completion.IsFaulted.ShouldBeTrue();
        sibling.Offers.ShouldBe(0);
        await Should.ThrowAsync<AggregateException>(() => appExecution.DisposeAsync().AsTask().WaitAsync(TestDeadline));
        (await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask())).GetBaseException().ShouldBeSameAs(error);
    }

    private sealed class ManualRouteSource : IFlowNode
    {
        private readonly ISourceBlock<FlowMessage> _output;
        public ManualRouteSource(bool native)
        {
            _output = native ? new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 2 })
                : new BroadcastBlock<FlowMessage>(static value => value, new DataflowBlockOptions { BoundedCapacity = 2 });
        }
        public ISourceBlock<FlowMessage> Output => _output;
        public Task Completion => _output.Completion;
        public void Complete() => _output.Complete();
        public void Fault(Exception error) => _output.Fault(error);
        public async ValueTask DisposeAsync()
        {
            Complete();
            await Completion;
        }
    }
}
