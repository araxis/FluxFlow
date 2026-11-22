using System.Text.Json;

namespace FluxFlow.Environments;

public sealed partial record EnvironmentTopology
{
    /// <summary>Adds an independent host snapshot to this topology and returns the placed host's ID.</summary>
    public string PlaceHost(InfrastructureHost source, double x, double y, IEnumerable<IResourceEnvironmentAdapter>? adapters = null)
    {
        source.Validate(adapters);
        var snapshot = source.CreateTopology();
        var host = snapshot.Nodes.Single(node => node.Kind == EnvironmentNodeKind.Host);
        var ids = snapshot.Nodes.ToDictionary(node => node.Id, _ => Guid.NewGuid().ToString("N"));
        var placed = new List<EnvironmentNode>(snapshot.Nodes.Count);
        foreach (var node in snapshot.Nodes)
        {
            var left = x + (node.X - host.X);
            var top = y + (node.Y - host.Y);
            if (!double.IsFinite(left) || !double.IsFinite(top))
                throw new ArgumentException("The host and its contents must have finite canvas positions.");
            placed.Add(node with { Id = ids[node.Id], InfrastructureGroup = ids[host.Id],
                X = left, Y = top,
                Host = node.Host is null ? null : ids[node.Host], Networks = node.Networks.Select(id => ids[id]).ToList() });
        }
        Nodes.AddRange(placed);
        return ids[host.Id];
    }

    public bool SetLocalExecutionHost(string hostId, bool local)
    {
        var selected = Nodes.FindIndex(node => node.Id == hostId && node.Kind == EnvironmentNodeKind.Host);
        if (selected < 0) throw new ArgumentException("The execution host is no longer available.");
        if (Nodes[selected].LocalHost == local) return false;

        if (local)
        {
            var previous = Nodes.FindIndex(node => node.LocalHost);
            if (previous >= 0) Nodes[previous] = Nodes[previous] with { LocalHost = false };
        }
        Nodes[selected] = Nodes[selected] with { LocalHost = local };
        return true;
    }

    public IEnumerable<EnvironmentNode> HostChoices(EnvironmentNode node) =>
        Nodes.Where(host => host.Kind == EnvironmentNodeKind.Host &&
            (host.InfrastructureGroup is null || node.Kind == EnvironmentNodeKind.Application || node.Host == host.Id));

    public static bool AllowsDefaultHost(EnvironmentNode node) =>
        node.Kind != EnvironmentNodeKind.Application || node.Host is null;

    public bool SetHost(string nodeId, string? hostId)
    {
        var node = Nodes.Find(item => item.Id == nodeId);
        if (node is null || node.Kind is not (EnvironmentNodeKind.Application or EnvironmentNodeKind.Service) || node.InfrastructureGroup is not null)
            throw new ArgumentException("The selected placement is unavailable or read-only.");
        if (hostId is null ? !AllowsDefaultHost(node) : !HostChoices(node).Any(host => host.Id == hostId))
            throw new ArgumentException("Choose an available execution host.");
        if (node.Host == hostId) return false;

        node.Host = hostId;
        node.Networks.RemoveAll(id => Nodes.Any(network => network.Id == id && network.InfrastructureGroup is { } group && group != hostId));
        return true;
    }

    /// <summary>Validates a proposed replacement and protects service definitions in existing placements.</summary>
    public void ValidateEdit(EnvironmentTopology after, IEnumerable<IResourceEnvironmentAdapter>? adapters = null)
    {
        after.Validate(adapters);
        foreach (var group in Nodes.Where(node => node.InfrastructureGroup is not null).GroupBy(node => node.InfrastructureGroup!))
        {
            var remaining = after.Nodes.Where(node => node.InfrastructureGroup == group.Key).ToArray();
            if (remaining.Length == 0 && !after.Nodes.Any(node => group.Any(original => original.Id == node.Id))) continue;
            var moved = remaining.SingleOrDefault(node => node.Id == group.Key);
            if (moved is null || remaining.Length != group.Count()) throw new ArgumentException("Remove the entire infrastructure placement, not its contents.");
            foreach (var original in group)
            {
                var candidate = remaining.SingleOrDefault(node => node.Id == original.Id);
                // Host placement geometry and companion binding belong to the project; service definitions stay locked.
                var normalized = candidate is null ? null : candidate with { X = original.X, Y = original.Y,
                    PlacementScale = original.Id == group.Key ? original.PlacementScale : candidate.PlacementScale,
                    Width = original.Id == group.Key ? original.Width : candidate.Width,
                    Height = original.Id == group.Key ? original.Height : candidate.Height,
                    LocalHost = original.Kind == EnvironmentNodeKind.Host ? original.LocalHost : candidate.LocalHost };
                if (candidate is null || Serialize(original) != Serialize(normalized!))
                    throw new ArgumentException("Saved host service settings are read-only. Edit their definition in Host definitions.");
            }
        }
    }

    private static string Serialize(EnvironmentNode node) => JsonSerializer.Serialize(
        node, EnvironmentJsonContext.Default.EnvironmentNode);
}
