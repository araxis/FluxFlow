namespace FluxFlow.Core;

/// <summary>Orders component dependencies once during graph preparation; never schedules work.</summary>
internal static class FlowDependencyOrder
{
    internal static T[][]? TryCreate<T>(IReadOnlyList<T> nodes, IEnumerable<(T Source, T Target)> edges)
        where T : notnull
    {
        var indices = nodes.Select((node, index) => (node, index)).ToDictionary(item => item.node, item => item.index);
        var outgoing = nodes.Select(_ => new HashSet<int>()).ToArray();
        var incoming = new int[nodes.Count];
        foreach (var (source, target) in edges)
        {
            var from = indices[source];
            var to = indices[target];
            if (outgoing[from].Add(to)) incoming[to]++;
        }

        var ready = Enumerable.Range(0, nodes.Count).Where(index => incoming[index] == 0).ToArray();
        var layers = new List<T[]>();
        var visited = 0;
        while (ready.Length != 0)
        {
            layers.Add(ready.Select(index => nodes[index]).ToArray());
            visited += ready.Length;
            var next = new List<int>();
            foreach (var source in ready)
                foreach (var target in outgoing[source])
                    if (--incoming[target] == 0) next.Add(target);
            next.Sort();
            ready = next.ToArray();
        }
        return visited == nodes.Count ? layers.ToArray() : null;
    }
}
