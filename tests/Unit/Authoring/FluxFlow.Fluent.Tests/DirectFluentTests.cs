using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Fluent.Tests;

public sealed class DirectFluentTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Named_components_keep_their_addresses_and_shared_fanout_envelope()
    {
        var message = FlowMessage.Create(FlowValue.From(new { Value = "owned" }));
        var side = new MessageSink();
        var main = new MessageSink();
        await using var graph = Flow.From(new LifecycleSource(message), "source")
            .Tap(side, "side")
            .To(main, "main")
            .Build();

        graph.Application.GetInput("main.side.Input").Address.ShouldBe("main.side.Input");
        graph.Application.GetInput("main.main.Input").Address.ShouldBe("main.main.Input");
        await graph.StartAsync();
        await graph.Completion.WaitAsync(Timeout);

        side.Message.ShouldBeSameAs(message);
        main.Message.ShouldBeSameAs(message);
    }

    [Fact]
    public async Task Built_graph_rejects_second_build_and_further_registration()
    {
        var builder = Flow.From(new StringSourceNode([]));
        await using var graph = builder.Build();
        await using var unused = new UppercaseNode();

        Should.Throw<InvalidOperationException>(() => builder.Build()).Message.ShouldContain("already been built");
        Should.Throw<InvalidOperationException>(() => builder.Then(unused)).Message.ShouldContain("already been built");
    }

    [Fact]
    public async Task Branch_rejects_foreign_and_undeclared_outputs_without_fake_components()
    {
        var node = new UndeclaredOutputNode();
        var builder = Flow.From(new StringSourceNode([])).Then(node);
        var foreign = new BufferBlock<FlowMessage>();
        Should.Throw<ArgumentException>(() => builder.Branch(foreign, _ => { })).Message.ShouldContain("declared output");
        Should.Throw<ArgumentException>(() => builder.Branch(node.Hidden, _ => { })).Message.ShouldContain("declared output");
        foreign.Complete();
        await using var graph = builder.Build();

        await graph.StartAsync();
        await graph.Completion.WaitAsync(Timeout);
        Should.Throw<KeyNotFoundException>(() => graph.Application.GetOutput("main.node0003.Output"));
    }

    [Fact]
    public async Task Segment_cannot_replace_its_graph_or_return_a_null_continuation()
    {
        var first = Flow.From(new StringSourceNode([]));
        var second = Flow.From(new StringSourceNode([]));
        var foreign = FlowSegment.Define("foreign", _ => second);
        var missing = FlowSegment.Define("missing", _ => null!);

        Should.Throw<InvalidOperationException>(() => first.Apply(foreign)).Message.ShouldContain("another graph");
        Should.Throw<InvalidOperationException>(() => first.Apply(missing)).Message.ShouldContain("null builder");
        await using var firstGraph = first.Build();
        await using var secondGraph = second.Build();
    }

    [Fact]
    public async Task Disposing_unstarted_graph_disposes_owned_source_once_without_running_it()
    {
        var source = new LifecycleSource(FlowMessage.Create(FlowValue.From(1)));
        var graph = Flow.From(source).Build();

        await graph.DisposeAsync();
        await graph.DisposeAsync();

        source.Runs.ShouldBe(0);
        source.Disposals.ShouldBe(1);
        source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        await Should.ThrowAsync<InvalidOperationException>(async () => await graph.StartAsync());
    }

    [Fact]
    public async Task Source_entry_rejects_a_processing_component()
    {
        await using var node = new UppercaseNode();

        Should.Throw<ArgumentException>(() => Flow.From(node)).Message.ShouldContain("source component");
    }

    [Fact]
    public void Fluent_assembly_depends_on_core_without_optional_authoring_or_hosting()
    {
        var dependencies = typeof(FlowGraph).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();

        dependencies.ShouldContain("FluxFlow.Core");
        dependencies.ShouldNotContain("FluxFlow.Json");
        dependencies.ShouldNotContain("FluxFlow.Composition");
        dependencies.ShouldNotContain("FluxFlow.Mapping");
        dependencies.ShouldNotContain("FluxFlow.Engine");
        dependencies.ShouldNotContain("Microsoft.Extensions.DependencyInjection");
    }

    private sealed class LifecycleSource(FlowMessage message) : FlowSource
    {
        public int Runs { get; private set; }
        public int Disposals { get; private set; }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            Runs++;
            await EmitAsync(message, cancellationToken);
        }

        protected override ValueTask OnDisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MessageSink : FlowNode
    {
        public FlowMessage? Message { get; private set; }

        protected override Task ProcessAsync(FlowMessage message)
        {
            Message = message;
            return Task.CompletedTask;
        }
    }

    private sealed class UndeclaredOutputNode : FlowNode
    {
        public ISourceBlock<FlowMessage> Hidden { get; }

        public UndeclaredOutputNode() => Hidden = AddOutput<FlowMessage>();

        protected override Task ProcessAsync(FlowMessage message) => Task.CompletedTask;
    }
}
