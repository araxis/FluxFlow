using Blazor.Diagrams.Core.Geometry;
using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Environments.Topology;

namespace FluxFlow.Studio.UI.Features.Environments.Infrastructure;

// Preserve saved placement scaling while keeping layout edits in unscaled coordinates.
internal sealed class InfrastructurePlacementLayout
{
    private readonly TopologyNodeModel _root;
    private readonly Point _origin;
    private readonly Dictionary<TopologyNodeModel, (Point Position, Size Size)> _members;
    private readonly Dictionary<TopologyNodeModel, Point> _initialPositions;
    public double Scale { get; private set; }

    public InfrastructurePlacementLayout(TopologyNodeModel root, IEnumerable<TopologyNodeModel> members)
    {
        _root = root;
        _origin = root.Position;
        _members = members.ToDictionary(node => node, node => (node.Position, node.Size!));
        _initialPositions = _members.ToDictionary(pair => pair.Key, pair => pair.Value.Position);
        foreach (var node in _members.Keys) node.Placement = this;
        Apply(_origin.X, _origin.Y, root.Definition.PlacementScale ?? 1);
    }

    public EnvironmentNode Capture(TopologyNodeModel node)
    {
        var resized = node == _root &&
            (Math.Abs(node.Size!.Width / Scale - _members[node].Size.Width) > .000001 ||
             Math.Abs(node.Size.Height / Scale - _members[node].Size.Height) > .000001);
        // Once resized, persist the entire displayed frame, including an inferred legacy origin.
        return node.Definition with
        {
            X = resized ? node.Position.X : Coordinate(node, true),
            Y = resized ? node.Position.Y : Coordinate(node, false),
            Width = resized ? node.Size!.Width / Scale : node.Definition.Width,
            Height = resized ? node.Size!.Height / Scale : node.Definition.Height
        };
    }

    private double Coordinate(TopologyNodeModel node, bool horizontal)
    {
        var position = horizontal ? node.Position.X : node.Position.Y;
        var rootPosition = horizontal ? _root.Position.X : _root.Position.Y;
        var origin = horizontal ? _origin.X : _origin.Y;
        var initial = horizontal ? _initialPositions[node].X : _initialPositions[node].Y;
        var movement = (position - rootPosition) / Scale - (initial - origin);
        if (Math.Abs(movement) < .000001) movement = 0;
        return (horizontal ? node.Definition.X : node.Definition.Y) + rootPosition - origin + movement;
    }

    public void Attach(TopologyNodeModel node)
    {
        var position = new Point(_origin.X + (node.Position.X - _root.Position.X) / Scale,
            _origin.Y + (node.Position.Y - _root.Position.Y) / Scale);
        node.Definition.X = _root.Definition.X + position.X - _origin.X;
        node.Definition.Y = _root.Definition.Y + position.Y - _origin.Y;
        _members.Add(node, (position, node.Size!));
        _initialPositions.Add(node, position);
        node.Placement = this;
        node.Size = new Size(node.Size!.Width * Scale, node.Size.Height * Scale);
    }

    public void RestoreUnscaledLayout()
    {
        var captured = _members.Keys.ToDictionary(node => node, Capture);
        foreach (var (node, layout) in _members)
        {
            var saved = captured[node];
            node.Definition.X = saved.X;
            node.Definition.Y = saved.Y;
            node.Definition.PlacementScale = saved.PlacementScale;
            node.Definition.Width = saved.Width;
            node.Definition.Height = saved.Height;
            node.UpdatePositionSilently(saved.X - node.Position.X, saved.Y - node.Position.Y);
            node.Size = node == _root ? new Size(saved.Width ?? layout.Size.Width, saved.Height ?? layout.Size.Height) : layout.Size;
            node.Placement = null;
        }
    }

    public void SetContentSize(TopologyNodeModel node, Size size)
    {
        _members[node] = (_members[node].Position, size);
        node.Size = new Size(size.Width * Scale, size.Height * Scale);
    }

    private void Apply(double x, double y, double scale)
    {
        Scale = scale;
        foreach (var (node, layout) in _members)
        {
            node.UpdatePositionSilently(x + (layout.Position.X - _origin.X) * scale - node.Position.X,
                y + (layout.Position.Y - _origin.Y) * scale - node.Position.Y);
            node.Size = new Size(layout.Size.Width * scale, layout.Size.Height * scale);
            node.Refresh();
            node.ReinitializePorts();
            node.RefreshLinks();
        }
    }
}
