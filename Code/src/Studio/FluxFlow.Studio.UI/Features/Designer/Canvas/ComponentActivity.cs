using FluxFlow.Studio.UI.Features.Environments;

namespace FluxFlow.Studio.UI.Features.Designer.Canvas;

internal sealed class ComponentActivity
{
    private Guid? _runId;
    private long _sequence;
    private readonly HashSet<FlowNodeModel> _highlighted = [];

    public ComponentActivity(StudioRunSession run)
    {
        _runId = run.Status?.Id;
        _sequence = run.Traces.Records.LastOrDefault()?.Sequence ?? 0;
    }

    public void Update(DesignerGraphState graph, StudioRunSession run)
    {
        if (_runId != run.Status?.Id)
        {
            Clear();
            _runId = run.Status?.Id;
            _sequence = 0;
        }
        var latest = run.Traces.Records.LastOrDefault()?.Sequence ?? 0;
        var previous = _sequence;
        _sequence = Math.Max(_sequence, latest);
        if (run.Status?.State != "Running") { Clear(); return; }
        var addresses = run.RecentTraceComponents(graph, previous);
        if (addresses is null) { Clear(); return; }

        var nodes = graph.Diagram.Nodes.OfType<FlowNodeModel>().ToArray();
        foreach (var old in _highlighted.Where(node => !nodes.Contains(node)).ToArray())
        {
            old.ActivityVersion = 0;
            old.Refresh();
            _highlighted.Remove(old);
        }
        var names = addresses.Where(address => address.WorkflowName == graph.WorkflowName)
            .Select(address => address.ComponentName).ToHashSet(StringComparer.Ordinal);
        foreach (var node in nodes.Where(node => names.Contains(node.NodeName)))
        {
            // One finite pulse per visible component per poll, regardless of message count.
            node.ActivityVersion = latest;
            _highlighted.Add(node);
            node.Refresh();
        }
    }

    public void Clear()
    {
        foreach (var node in _highlighted)
        {
            node.ActivityVersion = 0;
            node.Refresh();
        }
        _highlighted.Clear();
    }
}
