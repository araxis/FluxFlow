using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Fluent.Tests;

public sealed class FlowOutputPolicyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false, "main.source.Output")]
    [InlineData(true, "main.transform.Output")]
    public async Task Unlinked_emitting_output_faults_with_its_address(bool transform, string address)
    {
        var builder = Flow.From(new StringSourceNode(["value"]), "source");
        if (transform) builder = builder.Then(new UppercaseNode(), "transform");
        var graph = builder.Build();

        await AssertUnmatchedOutputAsync(graph, address);
    }

    [Fact]
    public async Task Missing_branch_faults_even_when_another_branch_has_a_terminal()
    {
        var router = new EvenOddRouter();
        var graph = Flow.From(new IntSourceNode(2), "source")
            .Then(router, "router")
            .Branch(router.Even, branch => branch.Then(new IntToLabelNode())
                .To(new CollectSinkNode(new StringCollector())))
            .Build();

        await AssertUnmatchedOutputAsync(graph, "main.router.Odd");
    }

    [Fact]
    public async Task Explicit_discard_completes_an_emitting_continuation()
    {
        var collector = new StringCollector();
        var builder = Flow.From(new StringSourceNode(["first", "second"]))
            .Then(new CollectSinkNode(collector));
        builder.DiscardUnmatched().ShouldBeSameAs(builder);
        await using var graph = builder.Build();

        await graph.StartAsync();
        await graph.Completion.WaitAsync(Timeout);

        collector.Items.ShouldBe(["first", "second"]);
        graph.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Explicit_branch_discard_preserves_the_other_branch_values()
    {
        var collector = new StringCollector();
        var router = new EvenOddRouter();
        await using var graph = Flow.From(new IntSourceNode(4))
            .Then(router)
            .Branch(router.Even, branch => branch.Then(new IntToLabelNode())
                .To(new CollectSinkNode(collector)))
            .Branch(router.Odd, branch => branch.DiscardUnmatched())
            .Build();

        await graph.StartAsync();
        await graph.Completion.WaitAsync(Timeout);

        collector.Items.ShouldBe(["n=0", "n=2"]);
    }

    [Fact]
    public async Task Explicit_discard_does_not_hide_another_unlinked_output()
    {
        var router = new EvenOddRouter();
        var graph = Flow.From(new IntSourceNode(2))
            .Then(router, "router")
            .Branch(router.Even, branch => branch.DiscardUnmatched())
            .Build();

        await AssertUnmatchedOutputAsync(graph, "main.router.Odd");
    }

    [Fact]
    public async Task Explicit_discard_preserves_subsequent_links()
    {
        var collector = new StringCollector();
        await using var graph = Flow.From(new StringSourceNode(["first", "second"]))
            .DiscardUnmatched()
            .To(new CollectSinkNode(collector))
            .Build();

        await graph.StartAsync();
        await graph.Completion.WaitAsync(Timeout);

        collector.Items.ShouldBe(["first", "second"]);
    }

    [Fact]
    public async Task Built_graph_rejects_discard_policy_mutation()
    {
        var builder = Flow.From(new StringSourceNode([]));
        await using var graph = builder.Build();

        Should.Throw<InvalidOperationException>(() => builder.DiscardUnmatched())
            .Message.ShouldContain("already been built");
    }

    [Fact]
    public async Task Terminal_side_component_preserves_linked_outputs_and_discards_only_unmatched_ones()
    {
        var side = new StringCollector();
        var main = new StringCollector();
        var router = new EvenOddRouter();
        await using var graph = Flow.From(new IntSourceNode(4))
            .Tap(router)
            .Branch(router.Even, branch => branch.Then(new IntToLabelNode())
                .To(new CollectSinkNode(side)))
            .Then(new IntToLabelNode())
            .To(new CollectSinkNode(main))
            .Build();

        await graph.StartAsync();
        await graph.Completion.WaitAsync(Timeout);

        side.Items.ShouldBe(["n=0", "n=2"]);
        main.Items.ShouldBe(["n=0", "n=1", "n=2", "n=3"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Terminal_operations_accept_input_only_components(bool tap)
    {
        var sink = new InputOnlySink();
        var builder = Flow.From(new StringSourceNode(["first", "second"]));
        await using var graph = tap
            ? builder.Tap(sink).DiscardUnmatched().Build()
            : builder.To(sink).Build();

        await graph.StartAsync();
        await graph.Completion.WaitAsync(Timeout);

        sink.Collector.Items.ShouldBe(["first", "second"]);
        sink.Ports.Outputs.ShouldBeEmpty();
    }

    private sealed class InputOnlySink : FlowNode
    {
        public InputOnlySink() => Ports = new FlowPorts(
            inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = Input });

        public override FlowPorts Ports { get; }
        public StringCollector Collector { get; } = new();

        protected override Task ProcessAsync(FlowMessage message)
        {
            Collector.Add(message.Value!.Deserialize<string>()!);
            return Task.CompletedTask;
        }
    }

    private static async Task AssertUnmatchedOutputAsync(FlowGraph graph, string address)
    {
        try
        {
            await graph.StartAsync();
            var error = await Should.ThrowAsync<InvalidOperationException>(
                () => graph.Completion.WaitAsync(Timeout));
            error.Message.ShouldContain("No route");
            error.Message.ShouldContain(address);
        }
        finally
        {
            // Faulted Core disposal reports the already-observed execution failure.
            try { await graph.DisposeAsync().AsTask().WaitAsync(Timeout); }
            catch (AggregateException) when (graph.Completion.IsFaulted) { }
        }
    }
}
