using Shouldly;
using Xunit;

namespace FluxFlow.Fluent.Tests;

public sealed class FlowBuilderTests
{
    [Fact]
    public async Task Then_and_To_compile_to_canonical_components_catalog_ports_and_links()
    {
        await using var flow = Flow
            .From(new StringSourceNode(["value"]))
            .Then(new UppercaseNode())
            .To(new CollectSinkNode(new StringCollector()))
            .Build();

        var definition = flow.Definition;
        var components = definition.Workflows["main"].Components;

        components.Keys.ShouldBe(
            ["node0001", "node0002", "node0003"],
            ignoreOrder: true);
        components["node0001"].Type.ShouldBe("fluent.node.0001");
        components["node0002"].Type.ShouldBe("fluent.node.0002");
        components["node0003"].Type.ShouldBe("fluent.node.0003");
        definition.ComponentDescriptors.Select(static descriptor => descriptor.Type)
            .ShouldBe([
                "fluent.node.0001",
                "fluent.node.0002",
                "fluent.node.0003"
            ]);
        definition.ComponentDescriptors[0].Inputs.ShouldBeEmpty();
        definition.ComponentDescriptors[0].Outputs.Keys.ShouldBe(["Output", "Events"]);
        definition.ComponentDescriptors[1].Inputs.Keys.ShouldBe(["Input"]);
        definition.ComponentDescriptors[1].Outputs.Keys.ShouldBe(["Output", "Events"]);
        definition.ComponentDescriptors[2].Inputs.Keys.ShouldBe(["Input"]);
        definition.ComponentDescriptors[2].Outputs.Keys.ShouldBe(["Output", "Events"]);
        definition.Links.Count.ShouldBe(2);
        definition.Links[0].Source.Value.ShouldBe("main.node0001.Output");
        definition.Links[0].Target.Value.ShouldBe("main.node0002.Input");
        definition.Links[0].MessageType.ShouldBe(typeof(string));
        definition.Links[1].Source.Value.ShouldBe("main.node0002.Output");
        definition.Links[1].Target.Value.ShouldBe("main.node0003.Input");
        definition.Links[1].MessageType.ShouldBe(typeof(string));
        definition.ApplicationResourceContracts.ShouldBeEmpty();
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
    public async Task Graph_exposes_canonical_definition_application_and_aggregated_events_only()
    {
        await using var flow = Flow
            .From(new TickingSourceNode())
            .To(new CollectSinkNode(new StringCollector()))
            .Build();

        flow.Events.ShouldNotBeNull();
        flow.Application.ShouldNotBeNull();
        flow.Application.CurrentDefinition.ShouldBeNull();
        flow.Definition.Workflows.Keys.ShouldBe(["main"]);
        flow.Definition.Workflows["main"].Components.Count.ShouldBe(2);
        flow.Definition.ComponentDescriptors.Count.ShouldBe(2);
        flow.Definition.Links.ShouldHaveSingleItem();
        typeof(FlowGraph).GetProperty("Runtime").ShouldBeNull();

        await flow.StartAsync();

        flow.Application.CurrentDefinition.ShouldBeSameAs(flow.Definition);
        await flow.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        await flow.Completion.WaitAsync(TimeSpan.FromSeconds(5));
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
