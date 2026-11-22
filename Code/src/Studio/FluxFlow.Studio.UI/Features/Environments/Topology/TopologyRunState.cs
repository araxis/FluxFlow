using FluxFlow.Environments;
using MudBlazor;

namespace FluxFlow.Studio.UI.Features.Environments.Topology;

public sealed record TopologyRunState(string Label, string Detail, Color Color)
{
    internal static TopologyRunState? Resolve(EnvironmentNode node, EnvironmentDefinition definition, EnvironmentRunStatus? status)
    {
        if (status is null || node.Kind is not (EnvironmentNodeKind.Application or EnvironmentNodeKind.Service)) return null;
        if (node.Kind == EnvironmentNodeKind.Service &&
            (node.Resource is null || definition.Setup.Resources.GetValueOrDefault(node.Resource)?.Provider is not { } provider || provider == "existing"))
            return new("External", "Existing service: its health and lifetime are not managed by this run.", Color.Default);
        return status.State switch
        {
            "Preparing" => new("Starting", "The run is preparing services and starting applications. Individual readiness is not reported.", Color.Info),
            "Running" when node.Kind == EnvironmentNodeKind.Service && !status.Services.Any(service => service.Resource.Equals(node.Resource, StringComparison.OrdinalIgnoreCase))
                => new("Not reported", "The run is running, but this service has no prepared endpoint in the companion response.", Color.Warning),
            "Running" => new("Running", "Reported by the companion for this run snapshot; not a continuous health check.", Color.Success),
            "Stopping" => new("Stopping", "The run is stopping applications and cleaning up temporary services.", Color.Info),
            "Stopped" => new("Stopped", "The run stopped and temporary services were cleaned up. Existing services were left untouched.", Color.Default),
            "Failed" => new("Run failed", "The run failed. Select this node for run-level failure details; the companion does not identify a failing node.", Color.Error),
            _ => new(status.State, "Run state reported by the companion.", Color.Default)
        };
    }
}
