namespace FluxFlow.Application.Execution;

/// <summary>Detects cycles in component links; does not order or schedule execution.</summary>
internal static class FlowCycleDetection
{
    internal static bool HasCycle<T>(IReadOnlyList<T> nodes, IEnumerable<(T Source, T Target)> edges)
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

        var ready = new Queue<int>(Enumerable.Range(0, nodes.Count).Where(index => incoming[index] == 0));
        var visited = 0;
        while (ready.TryDequeue(out var source))
        {
            visited++;
            foreach (var target in outgoing[source])
                if (--incoming[target] == 0) ready.Enqueue(target);
        }
        return visited != nodes.Count;
    }
}
