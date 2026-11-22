using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Observations;
using FluxFlow.Studio.UI.Features.Environments.Infrastructure;

namespace FluxFlow.Studio.UI.Features.Environments.Topology;

public sealed class TopologyNodeModel : NodeModel
{
    public TopologyNodeModel(EnvironmentNode definition, string icon, string subtitle)
        : base(new Point(definition.X, definition.Y))
    {
        Definition = definition;
        Icon = icon;
        Subtitle = subtitle;
        ControlledSize = IsContainer || definition.Kind == EnvironmentNodeKind.Application || definition.InfrastructureGroup is not null;
        Size = IsContainer ? new Size(definition.Width ?? 520, definition.Height ?? 360) : new Size(180, 60);
        RememberLayout();
    }
    public EnvironmentNode Definition { get; }
    public string Icon { get; }
    public string Subtitle { get; }
    public TopologyEndpoint? Endpoint { get; internal set; }
    public TopologyRunState? RunState { get; internal set; }
    public ApplicationTelemetryTarget? TelemetryTarget { get; internal set; }
    public Func<ApplicationTelemetryTarget, Task>? ShowLogs { get; internal set; }
    public Func<ApplicationTelemetryTarget, Task>? ShowTraces { get; internal set; }
    public string AccentColor => Definition.Color ?? DefaultColor(Definition.Kind);
    internal static string DefaultColor(EnvironmentNodeKind kind) => kind switch
    {
        EnvironmentNodeKind.Host => "#66b3e0",
        EnvironmentNodeKind.Network => "#62b6aa",
        EnvironmentNodeKind.Application => "#bc94e6",
        _ => "#d5af70"
    };
    internal int Sequence { get; set; }
    public bool IsContainer => Definition.Kind is EnvironmentNodeKind.Host or EnvironmentNodeKind.Network;
    public bool CanResize => IsContainer && (Definition.InfrastructureGroup is null || Definition.InfrastructureGroup == Definition.Id);
    internal InfrastructurePlacementLayout? Placement { get; set; }
    public double DisplayScale => Placement?.Scale ?? 1;
    public double BoundaryScale => CanResize ? 1 : DisplayScale;
    public TopologyNodeModel? Container { get; internal set; }
    public List<TopologyNodeModel> Children { get; } = [];
    public Action<TopologyNodeModel, string, Microsoft.AspNetCore.Components.Web.PointerEventArgs>? ResizeStarted { get; set; }
    private Point _layoutPosition = Point.Zero;
    private Size _layoutSize = Size.Zero;
    internal bool HasMoved => !Position.Equals(_layoutPosition);
    private bool HasResized => IsContainer && !Size!.Equals(_layoutSize);
    internal void RememberLayout() { _layoutPosition = Position; _layoutSize = Size!; }
    internal EnvironmentNode Capture() => Placement?.Capture(this) ?? Definition with
    {
        X = HasMoved || HasResized ? Position.X : Definition.X,
        Y = HasMoved || HasResized ? Position.Y : Definition.Y,
        Width = IsContainer && (HasMoved || HasResized) ? Size!.Width : Definition.Width,
        Height = IsContainer && (HasMoved || HasResized) ? Size!.Height : Definition.Height
    };
    internal Point ConstrainChildOffset(double x, double y, double childWidth, double childHeight) =>
        new(Math.Clamp(x, 24, Math.Max(24, Size!.Width / DisplayScale - childWidth - 24)),
            Math.Clamp(y, 76, Math.Max(76, Size!.Height / DisplayScale - childHeight - 24)));

    public override void SetPosition(double x, double y)
    {
        if (Container is { } parent)
        {
            var scale = parent.DisplayScale;
            var offset = parent.ConstrainChildOffset((x - parent.Position.X) / scale, (y - parent.Position.Y) / scale,
                Size!.Width / scale, Size.Height / scale);
            x = parent.Position.X + offset.X * scale;
            y = parent.Position.Y + offset.Y * scale;
        }
        var dx = x - Position.X;
        var dy = y - Position.Y;
        base.SetPosition(x, y);
        foreach (var child in Children.Where(child => !child.Selected || child.Definition.InfrastructureGroup is not null))
            child.SetPosition(child.Position.X + dx, child.Position.Y + dy);
    }
    public void ResizeBounds(double left, double top, double right, double bottom, string edge = "nw")
    {
        if (!CanResize) return;
        var scale = DisplayScale;
        if (edge.Contains('w')) left = Math.Min(left, right - 240 * scale);
        else right = Math.Max(right, left + 240 * scale);
        if (edge.Contains('n')) top = Math.Min(top, bottom - 180 * scale);
        else bottom = Math.Max(bottom, top + 180 * scale);
        foreach (var child in Children)
        {
            left = Math.Min(left, child.Position.X - 24 * scale);
            top = Math.Min(top, child.Position.Y - 76 * scale);
            right = Math.Max(right, child.Position.X + child.Size!.Width + 24 * scale);
            bottom = Math.Max(bottom, child.Position.Y + child.Size!.Height + 24 * scale);
        }
        if (Container is { } parent)
        {
            left = Math.Max(left, parent.Position.X + 24 * parent.DisplayScale);
            top = Math.Max(top, parent.Position.Y + 76 * parent.DisplayScale);
            right = Math.Min(right, parent.Position.X + parent.Size!.Width - 24 * parent.DisplayScale);
            bottom = Math.Min(bottom, parent.Position.Y + parent.Size!.Height - 24 * parent.DisplayScale);
        }
        UpdatePositionSilently(left - Position.X, top - Position.Y);
        Resize(right - left, bottom - top);
    }
    public void Resize(double width, double height)
    {
        var scale = DisplayScale;
        Size = new Size(Math.Max(Math.Max(240 * scale, width), Children.Select(child => child.Position.X + child.Size!.Width - Position.X + 24 * scale).DefaultIfEmpty().Max()),
            Math.Max(Math.Max(180 * scale, height), Children.Select(child => child.Position.Y + child.Size!.Height - Position.Y + 24 * scale).DefaultIfEmpty().Max()));
        Refresh();
        ReinitializePorts();
        RefreshLinks();
    }
    internal void SetContentSize(Size size)
    {
        if (Placement is not null) Placement.SetContentSize(this, size);
        else Size = size;
    }
    public IEnumerable<TopologyConnectionPort> ConnectionPorts => Ports.OfType<TopologyConnectionPort>();
    public void AddConnectionPort(string path, string type, string label)
    {
        AddPort(new TopologyConnectionPort(this, path, type, label));
        Size = new Size(180, 60 + ConnectionPorts.Count() * 24);
    }
}
