using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class FlowDependencyOrderTests
{
    [Fact]
    public void Layers_preserve_input_order_and_wait_for_all_unique_upstreams()
    {
        string[] nodes = ["join", "late", "second", "first", "isolated"];
        (string, string)[] edges = [("first", "join"), ("first", "late"), ("second", "join"),
            ("late", "join"), ("first", "late")];
        var layers = FlowDependencyOrder.TryCreate(nodes, edges).ShouldNotBeNull();
        layers.Length.ShouldBe(3);
        layers[0].ShouldBe(new[] { "second", "first", "isolated" });
        layers[1].ShouldBe(new[] { "late" });
        layers[2].ShouldBe(new[] { "join" });
        layers.SelectMany(layer => layer).Distinct().Count().ShouldBe(nodes.Length);
        nodes.ShouldBe(new[] { "join", "late", "second", "first", "isolated" });
        edges.Length.ShouldBe(5);
    }

    [Fact]
    public void Empty_graph_has_no_layers()
        => FlowDependencyOrder.TryCreate(Array.Empty<int>(), Array.Empty<(int, int)>()).ShouldNotBeNull().ShouldBeEmpty();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Cycles_never_produce_a_partial_plan(int shape)
    {
        int[] nodes = [0, 1, 2, 3];
        (int, int)[] edges = shape == 0 ? [(1, 1)]
            : shape == 1 ? [(0, 1), (1, 0)] : [(0, 1), (2, 3), (3, 2)];
        FlowDependencyOrder.TryCreate(nodes, edges).ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dependency_endpoints_must_belong_to_the_graph(bool missingSource)
    {
        (int, int)[] edges = missingSource ? [(9, 1)] : [(1, 9)];
        Should.Throw<KeyNotFoundException>(() => FlowDependencyOrder.TryCreate(new[] { 1, 2 }, edges));
    }

    [Fact]
    public void Duplicate_node_identities_are_rejected()
        => Should.Throw<ArgumentException>(() => FlowDependencyOrder.TryCreate(new[] { 1, 1 }, Array.Empty<(int, int)>()));
}
