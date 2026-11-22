using Blazor.Diagrams.Core.Geometry;
using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Environments.Infrastructure;

namespace FluxFlow.Studio.UI.Features.Environments.Topology;

internal static class TopologyContainers
{
    public static void Arrange(IReadOnlyList<TopologyNodeModel> nodes)
    {
        foreach (var placement in nodes.Select(node => node.Placement).OfType<InfrastructurePlacementLayout>().Distinct())
            placement.RestoreUnscaledLayout();
        foreach (var node in nodes) { node.Container = null; node.Children.Clear(); }
        foreach (var node in nodes.Where(node => node.Definition.Kind != EnvironmentNodeKind.Host))
        {
            var parent = node.Definition.Kind != EnvironmentNodeKind.Network
                ? node.Definition.Networks.Select(id => nodes.FirstOrDefault(network => network.Definition.Id == id &&
                    network.Definition.Kind == EnvironmentNodeKind.Network && network.Definition.Host == node.Definition.Host)).FirstOrDefault(network => network is not null)
                : null;
            parent ??= nodes.FirstOrDefault(host => host.Definition.Id == node.Definition.Host);
            node.Container = parent;
            parent?.Children.Add(node);
        }
        // Older projects stored symbols without container dimensions. Frame their existing members in the view.
        foreach (var container in nodes.Where(node => node.IsContainer).OrderBy(node => node.Definition.Kind == EnvironmentNodeKind.Host))
        {
            if (container.Placement is null && container.Definition.Width is null && container.Children.Count > 0)
            {
                var left = container.Children.Min(child => child.Position.X) - 24;
                var top = container.Children.Min(child => child.Position.Y) - 76;
                var right = container.Children.Max(child => child.Position.X + child.Size!.Width) + 24;
                var bottom = container.Children.Max(child => child.Position.Y + child.Size!.Height) + 24;
                container.UpdatePositionSilently(left - container.Position.X, top - container.Position.Y);
                container.Size = new Size(Math.Max(280, right - left), Math.Max(200, bottom - top));
            }
        }
        foreach (var node in nodes) node.RememberLayout();
        foreach (var group in nodes.Where(node => node.Definition.InfrastructureGroup is not null).GroupBy(node => node.Definition.InfrastructureGroup))
            _ = new InfrastructurePlacementLayout(group.Single(node => node.Definition.Id == group.Key),
                group.Concat(nodes.Where(node => node.Definition.Kind == EnvironmentNodeKind.Application && node.Definition.Host == group.Key)));
    }

    public static void Drop(TopologyNodeModel node, IReadOnlyList<TopologyNodeModel> nodes)
    {
        if (node.Definition.Kind == EnvironmentNodeKind.Host || node.Definition.InfrastructureGroup is not null) return;
        var center = new Point(node.Position.X + node.Size!.Width / 2, node.Position.Y + Math.Min(80, node.Size.Height / 2));
        var network = node.Definition.Kind == EnvironmentNodeKind.Network ? null :
            Find(nodes, EnvironmentNodeKind.Network, center, node);
        // Containment is automatic, but a host member can still be dropped into one of its networks.
        if (node.Container is { } container &&
            (container.Definition.Kind != EnvironmentNodeKind.Host || network is null || network.Definition.Host != container.Definition.Id))
        {
            KeepInside(node);
            return;
        }
        var host = Find(nodes, EnvironmentNodeKind.Host, center, node);
        if (network?.Definition.Host is { } networkHost)
            host = nodes.FirstOrDefault(candidate => candidate.Definition.Id == networkHost);
        node.Definition.Host = host?.Definition.Id;
        if (node.Container?.Definition.Kind == EnvironmentNodeKind.Network)
            node.Definition.Networks.Remove(node.Container.Definition.Id);
        if (network is not null && !node.Definition.Networks.Contains(network.Definition.Id))
            node.Definition.Networks.Insert(0, network.Definition.Id);
        if (node.Definition.Kind == EnvironmentNodeKind.Network)
            foreach (var child in node.Children) child.Definition.Host = node.Definition.Host;
        node.Container?.Children.Remove(node);
        node.Container = network ?? host;
        node.Container?.Children.Add(node);
        if (node.Placement is null && node.Container?.Placement is { } placement) placement.Attach(node);
        KeepInside(node);
    }

    private static TopologyNodeModel? Find(IReadOnlyList<TopologyNodeModel> nodes, EnvironmentNodeKind kind, Point point, TopologyNodeModel exclude) =>
        nodes.Where(node => node != exclude && (node.Definition.InfrastructureGroup is null || exclude.Definition.Kind == EnvironmentNodeKind.Application) && node.Definition.Kind == kind && point.X >= node.Position.X &&
            point.X <= node.Position.X + node.Size!.Width && point.Y >= node.Position.Y + 52 * node.DisplayScale && point.Y <= node.Position.Y + node.Size.Height)
            .OrderBy(node => node.Size!.Width * node.Size.Height).FirstOrDefault();

    public static void KeepInside(TopologyNodeModel node)
    {
        if (node.Container is not { } parent) return;
        node.SetPosition(Math.Max(parent.Position.X + 24 * parent.DisplayScale, node.Position.X), Math.Max(parent.Position.Y + 76 * parent.DisplayScale, node.Position.Y));
        if (parent.Placement is null) parent.Resize(parent.Size!.Width, parent.Size.Height);
        KeepInside(parent);
    }
}
