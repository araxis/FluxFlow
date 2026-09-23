using FluxFlow.Core.Execution;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class FlowCycleDetectionTests
{
    [Fact]
    public void Acyclic_links_with_multiple_upstreams_duplicates_and_isolated_nodes_are_accepted()
    {
        string[] nodes = ["join", "late", "second", "first", "isolated"];
        (string, string)[] edges = [("first", "join"), ("first", "late"), ("second", "join"),
            ("late", "join"), ("first", "late")];
        FlowCycleDetection.HasCycle(nodes, edges).ShouldBeFalse();
        nodes.ShouldBe(new[] { "join", "late", "second", "first", "isolated" });
        edges.Length.ShouldBe(5);
    }

    [Fact]
    public void Empty_graph_has_no_cycle()
        => FlowCycleDetection.HasCycle(Array.Empty<int>(), Array.Empty<(int, int)>()).ShouldBeFalse();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Self_loops_connected_and_disconnected_cycles_are_detected(int shape)
    {
        int[] nodes = [0, 1, 2, 3];
        (int, int)[] edges = shape == 0 ? [(1, 1)]
            : shape == 1 ? [(0, 1), (1, 0)] : [(0, 1), (2, 3), (3, 2)];
        FlowCycleDetection.HasCycle(nodes, edges).ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dependency_endpoints_must_belong_to_the_graph(bool missingSource)
    {
        (int, int)[] edges = missingSource ? [(9, 1)] : [(1, 9)];
        Should.Throw<KeyNotFoundException>(() => FlowCycleDetection.HasCycle(new[] { 1, 2 }, edges));
    }

    [Fact]
    public void Duplicate_node_identities_are_rejected()
        => Should.Throw<ArgumentException>(() => FlowCycleDetection.HasCycle(new[] { 1, 1 }, Array.Empty<(int, int)>()));
}
