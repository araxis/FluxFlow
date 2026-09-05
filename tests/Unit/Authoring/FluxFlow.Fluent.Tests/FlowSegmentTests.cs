using Shouldly;
using Xunit;

namespace FluxFlow.Fluent.Tests;

public sealed class FlowSegmentTests
{
    [Fact]
    public async Task Apply_splices_a_named_segment_into_the_chain()
    {
        var collector = new StringCollector();
        var shout = FlowSegment.Define<string, string>(
            "shout",
            builder => builder.Then(new UppercaseNode()).Then(new ExclaimNode()));

        await using var flow = Flow
            .From(new StringSourceNode(["hello", "world"]))
            .Apply(shout)
            .To(new CollectSinkNode(collector))
            .Build();

        await flow.StartAsync();
        await flow.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        collector.Items.ShouldBe(["HELLO!", "WORLD!"]);
    }

    [Fact]
    public async Task The_same_segment_can_be_reused_across_independent_flows()
    {
        var shout = FlowSegment.Define<string, string>(
            "shout",
            builder => builder.Then(new UppercaseNode()).Then(new ExclaimNode()));

        var first = new StringCollector();
        var second = new StringCollector();

        await using var flowA = Flow.From(new StringSourceNode(["a"])).Apply(shout).To(new CollectSinkNode(first)).Build();
        await using var flowB = Flow.From(new StringSourceNode(["b"])).Apply(shout).To(new CollectSinkNode(second)).Build();

        await flowA.StartAsync();
        await flowB.StartAsync();
        await flowA.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await flowB.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // Each application built fresh node instances, so the two flows run independently.
        first.Items.ShouldBe(["A!"]);
        second.Items.ShouldBe(["B!"]);
    }

    [Fact]
    public async Task A_segment_can_change_the_payload_type()
    {
        var collector = new StringCollector();
        var label = FlowSegment.Define<int, string>(
            "label",
            builder => builder.Then(new IntToLabelNode()));

        await using var flow = Flow
            .From(new IntSourceNode(2))
            .Apply(label)
            .To(new CollectSinkNode(collector))
            .Build();

        await flow.StartAsync();
        await flow.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        collector.Items.ShouldBe(["n=0", "n=1"]);
    }

    [Fact]
    public async Task Segments_compose_with_each_other_in_a_chain()
    {
        var collector = new StringCollector();
        var upper = FlowSegment.Define<string, string>("upper", b => b.Then(new UppercaseNode()));
        var exclaim = FlowSegment.Define<string, string>("exclaim", b => b.Then(new ExclaimNode()));

        await using var flow = Flow
            .From(new StringSourceNode(["hi"]))
            .Apply(upper)
            .Apply(exclaim)
            .To(new CollectSinkNode(collector))
            .Build();

        await flow.StartAsync();
        await flow.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        collector.Items.ShouldBe(["HI!"]);
    }

    [Fact]
    public void Apply_rejects_a_null_segment()
    {
        var builder = Flow.From(new StringSourceNode(["x"]));
        Should.Throw<ArgumentNullException>(() => builder.Apply<string>(null!));
    }

    [Fact]
    public void Segment_rejects_a_blank_name_or_null_build()
    {
        Should.Throw<ArgumentException>(() => FlowSegment.Define<string, string>(" ", b => b));
        Should.Throw<ArgumentNullException>(() => FlowSegment.Define<string, string>("ok", null!));
    }
}
