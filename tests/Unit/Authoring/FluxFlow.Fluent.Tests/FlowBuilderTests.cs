using Shouldly;
using Xunit;

namespace FluxFlow.Fluent.Tests;

public sealed class FlowBuilderTests
{
    [Fact]
    public async Task Then_and_To_register_real_ports_on_the_direct_Core_application()
    {
        var source = new StringSourceNode(["value"]);
        var transform = new UppercaseNode();
        var collector = new StringCollector();
        var sink = new CollectSinkNode(collector);
        await using var flow = Flow.From(source).Then(transform).To(sink).Build();

        flow.Application.GetOutput("main.node0001.Output").Address.ShouldBe("main.node0001.Output");
        flow.Application.GetInput("main.node0002.Input").Address.ShouldBe("main.node0002.Input");
        flow.Application.GetOutput("main.node0002.Output").Address.ShouldBe("main.node0002.Output");
        flow.Application.GetInput("main.node0003.Input").Address.ShouldBe("main.node0003.Input");
        source.Ports.Outputs["Output"].ShouldBeSameAs(source.Output);
        transform.Ports.Inputs["Input"].ShouldBeSameAs(transform.Input);
        sink.Ports.Inputs["Input"].ShouldBeSameAs(sink.Input);

        await flow.StartAsync();
        flow.Completion.ShouldBeSameAs(flow.Application.Completion);
        await flow.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        collector.Items.ShouldBe(["VALUE"]);
    }

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
    public async Task Graph_exposes_the_direct_application_and_native_events_without_an_executable_definition()
    {
        await using var flow = Flow
            .From(new TickingSourceNode())
            .To(new CollectSinkNode(new StringCollector()))
            .Build();

        flow.Events.ShouldNotBeNull();
        flow.Application.ShouldBeOfType<FluxFlow.Core.FlowApplication>();
        flow.Application.GetInput("main.node0002.Input").Address.ShouldBe("main.node0002.Input");
        typeof(FlowGraph).GetProperty("Definition").ShouldBeNull();
        typeof(FlowGraph).GetProperty("Runtime").ShouldBeNull();

        await flow.StartAsync();
        flow.Completion.ShouldBeSameAs(flow.Application.Completion);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await flow.StopAsync(deadline.Token);
        await flow.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void From_rejects_a_null_source()
        => Should.Throw<ArgumentNullException>(() => Flow.From(null!));

    [Fact]
    public void Then_rejects_a_null_node()
    {
        var builder = Flow.From(new StringSourceNode(["x"]));
        Should.Throw<ArgumentNullException>(() => builder.Then(null!));
    }
}
