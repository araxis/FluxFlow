using Shouldly;
using Xunit;

namespace FluxFlow.Fluent.Tests;

public sealed class FlowBuilderTests
{
    [Fact]
    public async Task Linear_flow_runs_source_through_node_to_sink()
    {
        var collector = new StringCollector();

        await using var flow = Flow
            .From(new StringSourceNode(["hello", "world"]))
            .Then(new UppercaseNode())
            .To(new CollectSinkNode(collector))
            .Build();

        await flow.StartAsync();
        await flow.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        collector.Items.ShouldBe(["HELLO", "WORLD"]);
    }

    [Fact]
    public async Task Type_changing_chain_flows_int_source_to_string_sink()
    {
        var collector = new StringCollector();

        await using var flow = Flow
            .From(new IntSourceNode(3))
            .Then(new IntToLabelNode())
            .To(new CollectSinkNode(collector))
            .Build();

        await flow.StartAsync();
        await flow.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        collector.Items.ShouldBe(["n=0", "n=1", "n=2"]);
    }

    [Fact]
    public async Task Tap_fans_the_same_payload_to_a_side_sink_and_the_main_line()
    {
        var tapped = new StringCollector();
        var main = new StringCollector();

        await using var flow = Flow
            .From(new StringSourceNode(["a", "b"]))
            .Then(new UppercaseNode())
            .Tap(new CollectSinkNode(tapped))
            .To(new CollectSinkNode(main))
            .Build();

        await flow.StartAsync();
        await flow.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        main.Items.ShouldBe(["A", "B"]);
        tapped.Items.ShouldBe(["A", "B"]);
    }

    [Fact]
    public async Task Branches_route_by_port_and_fan_in_to_a_shared_sink()
    {
        var collector = new StringCollector();
        var sink = new CollectSinkNode(collector);
        var router = new EvenOddRouter();

        await using var flow = Flow
            .From(new IntSourceNode(4)) // 0, 1, 2, 3
            .Then(router)
            .Branch(router.Even, even => even.Then(new IntToLabelNode()).To(sink))
            .Branch(router.Odd, odd => odd.Then(new IntToLabelNode()).To(sink))
            .Build();

        await flow.StartAsync();
        await flow.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // Even and odd branches run concurrently, so arrival order is not defined; the set is.
        collector.Items.OrderBy(item => item, StringComparer.Ordinal)
            .ShouldBe(["n=0", "n=1", "n=2", "n=3"]);
    }

    [Fact]
    public async Task Branch_that_never_receives_a_message_still_lets_the_flow_complete()
    {
        var collector = new StringCollector();
        var router = new EvenOddRouter();

        await using var flow = Flow
            .From(new IntSourceNode(1)) // only 0 -> even; odd branch stays empty
            .Then(router)
            .Branch(router.Even, even => even.Then(new IntToLabelNode()).To(new CollectSinkNode(collector)))
            .Branch(router.Odd, odd => odd.Then(new IntToLabelNode()).To(new CollectSinkNode(new StringCollector())))
            .Build();

        await flow.StartAsync();
        await flow.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        collector.Items.ShouldBe(["n=0"]);
    }

    [Fact]
    public async Task StopAsync_completes_an_unbounded_source()
    {
        var collector = new StringCollector();

        await using var flow = Flow
            .From(new TickingSourceNode())
            .To(new CollectSinkNode(collector))
            .Build();

        await flow.StartAsync();
        await flow.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        flow.Completion.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Graph_exposes_aggregated_error_and_event_streams()
    {
        await using var flow = Flow
            .From(new StringSourceNode(["x"]))
            .To(new CollectSinkNode(new StringCollector()))
            .Build();

        flow.Errors.ShouldNotBeNull();
        flow.Events.ShouldNotBeNull();
        flow.Runtime.ShouldNotBeNull();
    }

    [Fact]
    public void From_rejects_a_null_source()
        => Should.Throw<ArgumentNullException>(() => Flow.From<string>(null!));

    [Fact]
    public void Then_rejects_a_null_node()
    {
        var builder = Flow.From(new StringSourceNode(["x"]));
        Should.Throw<ArgumentNullException>(() => builder.Then<string>(null!));
    }
}
