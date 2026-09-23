namespace FluxFlow.Core.Execution;

/// <summary>Evaluates ordinary routes before choosing a single fallback; does not deliver messages.</summary>
internal static class FlowRouteSelection
{
    internal static int Complete(Span<bool> selected, (int Matches, int Fallback) selection,
        string source, bool discardUnmatched)
    {
        if (selection.Matches != 0) return selection.Matches;
        if (selection.Fallback >= 0)
        {
            selected[selection.Fallback] = true;
            return 1;
        }
        if (!discardUnmatched) throw new InvalidOperationException($"No route accepted a message from '{source}'.");
        return 0;
    }

    internal static (int Matches, int Fallback) SelectOrdinary<TRoute, TState>(
        IReadOnlyList<TRoute> routes, Span<bool> selected, TState state,
        Func<TRoute, bool> isFallback, Func<TRoute, TState, bool> matches)
    {
        selected.Clear();
        var fallback = -1;
        var count = 0;
        for (var index = 0; index < routes.Count; index++)
        {
            var route = routes[index];
            if (isFallback(route))
            {
                if (fallback >= 0) throw new InvalidOperationException("An output can have only one fallback route.");
                fallback = index;
                continue;
            }
            var matched = matches(route, state);
            selected[index] = matched;
            if (matched) count++;
        }
        return (count, fallback);
    }
}
