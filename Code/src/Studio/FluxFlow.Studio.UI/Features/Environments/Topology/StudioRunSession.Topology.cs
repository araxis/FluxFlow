using System.Text.Json;
using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Observations;

namespace FluxFlow.Studio.UI.Features.Environments;

public sealed partial class StudioRunSession
{
    private long _runProjectGeneration;
    private string? _runEnvironmentId, _runTopology;
    private void CaptureTopology(DesignerGraphState graph)
    {
        _runProjectGeneration = graph.ProjectGeneration;
        _runEnvironmentId = graph.ActiveEnvironmentId;
        _runTopology = TopologySnapshot(graph);
    }

    internal EnvironmentRunStatus? CurrentTopologyStatus(DesignerGraphState graph) =>
        ReferenceEquals(_runGraph, graph) && graph.IsProjectOpen &&
        graph.ProjectGeneration == _runProjectGeneration && graph.ActiveEnvironmentId == _runEnvironmentId &&
        graph.ExportConfiguration() == _runConfiguration && TopologySnapshot(graph) == _runTopology ? Status : null;

    internal ApplicationTelemetryTarget? ResolveApplicationTelemetry(DesignerGraphState graph, string instanceId, Guid runId) =>
        CurrentTopologyStatus(graph)?.Id == runId && graph.GetEnvironmentDefinition().Topology.Nodes
            .FirstOrDefault(node => node.Id == instanceId && node.Kind == EnvironmentNodeKind.Application) is { } application
                ? new(runId, application.Id, application.Name) : null;

    private static string TopologySnapshot(DesignerGraphState graph)
    {
        var definition = graph.GetEnvironmentDefinition();
        definition.Name = "";
        foreach (var node in definition.Topology.Nodes)
        {
            // Presentation edits do not change the running deployment.
            node.Name = "";
            node.Color = null;
            node.X = node.Y = 0;
            node.Width = node.Height = null;
            node.PlacementScale = null;
            node.KeepInsideContainer = false;
        }
        return JsonSerializer.Serialize(definition, EnvironmentJsonContext.Default.EnvironmentDefinition);
    }
}
