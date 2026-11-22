namespace FluxFlow.Studio.UI.Features.Designer.Components.Workspace;

// Layout only: panels stay mounted while their rectangles change.
public sealed class DockLayout
{
    public sealed class Node(string? panel = null)
    {
        public List<string> Panels { get; } = panel is null ? [] : [panel];
        public string? Panel => Panels.FirstOrDefault();
        public string? ActivePanel { get; set; } = panel;
        public bool Horizontal { get; init; }
        public double Percent { get; set; } = 50;
        public Node? First { get; set; }
        public Node? Second { get; set; }
    }
    public readonly record struct Area(double X, double Y, double Width, double Height);
    private sealed record Placement(DockPosition Edge, Node Neighbor, string NeighborPanel, int TabIndex, double Percent, double Size);
    private readonly Dictionary<string, Placement> _autoHidden = [];
    public IEnumerable<(string Name, DockPosition Edge)> AutoHiddenPanels => _autoHidden.Select(item => (item.Key, item.Value.Edge));
    public bool IsAutoHidden(string panel) => _autoHidden.ContainsKey(panel);
    private readonly Func<Node> _initial;
    public DockLayout(Func<Node>? initial = null) { _initial = initial ?? Default; Root = _initial(); }
    public Node Root { get; private set; }
    private static Node Default() => new()
    {
        Percent = 70,
        First = new() { Horizontal = true, Percent = 42,
            First = new() { First = new("Input"), Second = new("Message") }, Second = new("Expression") },
        Second = new("Result")
    };
    public void Reset() { _autoHidden.Clear(); Root = _initial(); }
    public bool Contains(string panel) => Find(Root, panel) is not null;
    public bool CanHide(string panel) => IsAutoHidden(panel) || (Contains(panel) && Visit().Sum(item => item.Node.Panels.Count) > 1);
    public void Hide(string panel)
    {
        if (_autoHidden.Remove(panel)) return;
        if (CanHide(panel)) Root = Remove(Root, panel)!;
    }
    public void AutoHide(string panel)
    {
        if (IsAutoHidden(panel) || !CanHide(panel)) return;
        var (group, area) = Visit().Single(item => item.Node.Panels.Contains(panel));
        var parent = Visit().Select(item => item.Node).FirstOrDefault(node => node.First == group || node.Second == group);
        var before = parent?.First == group;
        var edge = parent is null ? DockPosition.Left : parent.Horizontal
            ? (before ? DockPosition.Left : DockPosition.Right) : (before ? DockPosition.Top : DockPosition.Bottom);
        var tabbed = group.Panels.Count > 1;
        var neighbor = tabbed ? group : before ? parent!.Second! : parent!.First!;
        var neighborPanel = tabbed ? group.Panels.First(name => name != panel) : FirstPanel(neighbor);
        _autoHidden.Add(panel, new(edge, neighbor, neighborPanel, tabbed ? group.Panels.IndexOf(panel) : -1, parent?.Percent ?? 50,
            edge is DockPosition.Left or DockPosition.Right ? area.Width : area.Height));
        Root = Remove(Root, panel)!;
    }
    private static string FirstPanel(Node node) => node.Panel ?? FirstPanel(node.First!);
    public void Pin(string panel)
    {
        if (!_autoHidden.Remove(panel, out var position)) return;
        var neighbor = Visit().Any(item => item.Node == position.Neighbor)
            ? position.Neighbor : Find(Root, position.NeighborPanel);
        if (position.TabIndex >= 0 && neighbor is not null)
        {
            neighbor.Panels.Insert(Math.Min(position.TabIndex, neighbor.Panels.Count), panel);
            neighbor.ActivePanel = panel;
            return;
        }
        Insert(panel, position.Edge, Root, neighbor);
        var group = Find(Root, panel);
        var parent = Visit().Select(item => item.Node).Single(node => node.First == group || node.Second == group);
        parent.Percent = position.Percent;
    }
    public Area PeekArea(string panel)
    {
        var position = _autoHidden[panel];
        var size = Math.Clamp(position.Size, 20, 70);
        return position.Edge switch
        {
            DockPosition.Left => new(0, 0, size, 100),
            DockPosition.Right => new(100 - size, 0, size, 100),
            DockPosition.Top => new(0, 0, 100, size),
            _ => new(0, 100 - size, 100, size)
        };
    }
    public void Show(string panel, DockPosition edge)
    {
        Pin(panel);
        if (Find(Root, panel) is { } group) { group.ActivePanel = panel; return; }
        Insert(panel, edge, Root);
    }
    public Area Preview(string panel, DockPosition edge, string? target = null)
    {
        var preview = CreatePreview(panel);
        preview.Dock(panel, edge, target);
        return preview.Visit().Single(item => item.Node.Panels.Contains(panel)).Area;
    }
    private DockLayout CreatePreview(string panel)
    {
        var preview = new DockLayout { Root = Copy(Root) };
        if (_autoHidden.TryGetValue(panel, out var position)) preview.Show(panel, position.Edge);
        return preview;
    }
    private static Node Copy(Node node)
    {
        var copy = new Node { Horizontal = node.Horizontal, Percent = node.Percent, ActivePanel = node.ActivePanel,
            First = node.First is null ? null : Copy(node.First), Second = node.Second is null ? null : Copy(node.Second) };
        copy.Panels.AddRange(node.Panels);
        return copy;
    }
    public Area PreviewGroup(string panel, string target)
    {
        var preview = CreatePreview(panel);
        preview.Group(panel, target);
        return preview.Visit().Single(item => item.Node.Panels.Contains(panel)).Area;
    }
    public void Group(string panel, string target)
    {
        if (panel == target || !Contains(target)) return;
        Pin(panel);
        var source = Find(Root, panel);
        var destination = Find(Root, target);
        if (source is null || destination is null || ReferenceEquals(source, destination)) return;
        Root = Remove(Root, panel)!;
        destination.Panels.Add(panel);
        destination.ActivePanel = panel;
    }
    public void Dock(string panel, DockPosition edge, string? target = null)
    {
        Pin(panel);
        if (panel == target || Find(Root, panel) is null
            || (target is not null && Find(Root, target) is null)) return;
        if (Visit().Sum(item => item.Node.Panels.Count) == 1) return;
        var remaining = Remove(Root, panel)!;
        Insert(panel, edge, remaining, target is null ? null : Find(remaining, target));
    }
    private void Insert(string panel, DockPosition edge, Node remaining, Node? destination = null)
    {
        var wholeWorkspace = destination is null;
        destination ??= remaining;
        var before = edge is DockPosition.Left or DockPosition.Top;
        var split = new Node { Horizontal = edge is DockPosition.Left or DockPosition.Right,
            Percent = wholeWorkspace ? (before ? 30 : 70) : 50,
            First = before ? new(panel) : destination, Second = before ? destination : new(panel) };
        Root = wholeWorkspace ? split : Replace(remaining, destination, split);
    }
    private static Node? Remove(Node node, string panel)
    {
        if (node.Panel is not null)
        {
            node.Panels.Remove(panel);
            if (node.ActivePanel == panel) node.ActivePanel = node.Panel;
            return node.Panels.Count == 0 ? null : node;
        }
        node.First = Remove(node.First!, panel);
        node.Second = Remove(node.Second!, panel);
        return node.First is null ? node.Second : node.Second is null ? node.First : node;
    }
    private static Node? Find(Node node, string panel) => node.Panels.Contains(panel) ? node
        : node.Panel is not null ? null : Find(node.First!, panel) ?? Find(node.Second!, panel);
    private static Node Replace(Node node, Node old, Node replacement)
    {
        if (ReferenceEquals(node, old)) return replacement;
        if (node.Panel is null) { node.First = Replace(node.First!, old, replacement); node.Second = Replace(node.Second!, old, replacement); }
        return node;
    }
    public IEnumerable<(Node Node, Area Area)> Visit() => Visit(Root, new(0, 0, 100, 100));
    private static IEnumerable<(Node Node, Area Area)> Visit(Node node, Area area)
    {
        yield return (node, area);
        if (node.Panel is not null) yield break;
        var fraction = Math.Clamp(node.Percent, 15, 85) / 100;
        var first = node.Horizontal ? area with { Width = area.Width * fraction } : area with { Height = area.Height * fraction };
        var second = node.Horizontal ? area with { X = area.X + first.Width, Width = area.Width - first.Width }
            : area with { Y = area.Y + first.Height, Height = area.Height - first.Height };
        foreach (var item in Visit(node.First!, first)) yield return item;
        foreach (var item in Visit(node.Second!, second)) yield return item;
    }
}
